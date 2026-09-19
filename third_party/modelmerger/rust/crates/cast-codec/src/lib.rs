use std::fmt;

const CAST_MAGIC: u32 = u32::from_le_bytes(*b"cast");
const CAST_HEADER_SIZE: usize = 16;
const NODE_HEADER_SIZE: usize = 24;
const PROPERTY_HEADER_SIZE: usize = 8;
const MAX_NODE_DEPTH: usize = 256;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DecodeLimits {
    pub max_nodes: usize,
    pub max_properties: usize,
    pub max_value_bytes: usize,
    pub max_text_bytes: usize,
}

impl Default for DecodeLimits {
    fn default() -> Self {
        Self {
            max_nodes: 1_000_000,
            max_properties: 4_000_000,
            max_value_bytes: 512 * 1024 * 1024,
            max_text_bytes: 128 * 1024 * 1024,
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct CastFile {
    pub version: u32,
    pub flags: u32,
    pub roots: Vec<CastNode>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct CastNode {
    pub identifier: u32,
    pub hash: u64,
    pub properties: Vec<CastProperty>,
    pub children: Vec<CastNode>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct CastProperty {
    pub name: String,
    pub values: PropertyValues,
}

#[derive(Debug, Clone, PartialEq)]
pub enum PropertyValues {
    Byte(Vec<u8>),
    Short(Vec<u16>),
    Integer32(Vec<u32>),
    Integer64(Vec<u64>),
    Float(Vec<f32>),
    Double(Vec<f64>),
    String(String),
    Vector2(Vec<[f32; 2]>),
    Vector3(Vec<[f32; 3]>),
    Vector4(Vec<[f32; 4]>),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CodecError {
    UnexpectedEndOfFile,
    InvalidMagic(u32),
    UnsupportedPropertyType([u8; 2]),
    InvalidUtf8,
    InvalidStringValueCount(u32),
    CountExceedsFile,
    LengthOverflow,
    TrailingData,
    InvalidNodeSize(u32),
    NestingTooDeep,
    ResourceLimitExceeded(DecodeResource),
    Cancelled,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DecodeResource {
    Nodes,
    Properties,
    ValueBytes,
    TextBytes,
}

impl fmt::Display for DecodeResource {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(match self {
            Self::Nodes => "node count",
            Self::Properties => "property count",
            Self::ValueBytes => "property values",
            Self::TextBytes => "text data",
        })
    }
}

impl fmt::Display for CodecError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::UnexpectedEndOfFile => formatter.write_str("unexpected end of Cast file"),
            Self::InvalidMagic(magic) => write!(formatter, "invalid Cast magic 0x{magic:08X}"),
            Self::UnsupportedPropertyType(property_type) => write!(
                formatter,
                "unsupported Cast property type {:?}",
                String::from_utf8_lossy(property_type)
            ),
            Self::InvalidUtf8 => formatter.write_str("Cast text is not valid UTF-8"),
            Self::InvalidStringValueCount(count) => {
                write!(
                    formatter,
                    "Cast string property contains {count} values instead of one"
                )
            }
            Self::CountExceedsFile => {
                formatter.write_str("Cast collection count exceeds file size")
            }
            Self::LengthOverflow => formatter.write_str("Cast node is too large to encode"),
            Self::TrailingData => formatter.write_str("Cast file contains trailing data"),
            Self::InvalidNodeSize(size) => write!(formatter, "Cast node has invalid size {size}"),
            Self::NestingTooDeep => formatter.write_str("Cast node nesting is too deep"),
            Self::ResourceLimitExceeded(resource) => {
                write!(
                    formatter,
                    "Cast {resource} exceeds the configured decode limit"
                )
            }
            Self::Cancelled => formatter.write_str("Cast decoding was cancelled"),
        }
    }
}

impl std::error::Error for CodecError {}

impl CastFile {
    pub fn decode(bytes: &[u8]) -> Result<Self, CodecError> {
        Self::decode_with_cancel(bytes, || false)
    }

    pub fn decode_with_cancel(
        bytes: &[u8],
        mut is_cancelled: impl FnMut() -> bool,
    ) -> Result<Self, CodecError> {
        Self::decode_with_limits_and_cancel(bytes, DecodeLimits::default(), &mut is_cancelled)
    }

    pub fn decode_with_limits(bytes: &[u8], limits: DecodeLimits) -> Result<Self, CodecError> {
        Self::decode_with_limits_and_cancel(bytes, limits, || false)
    }

    pub fn decode_with_limits_and_cancel(
        bytes: &[u8],
        limits: DecodeLimits,
        mut is_cancelled: impl FnMut() -> bool,
    ) -> Result<Self, CodecError> {
        let mut reader = Reader::new(bytes);
        let mut budget = DecodeBudget::new(limits);
        check_cancelled(&mut is_cancelled)?;
        let magic = reader.read_u32()?;
        if magic != CAST_MAGIC {
            return Err(CodecError::InvalidMagic(magic));
        }

        let version = reader.read_u32()?;
        let root_count = reader.read_u32()?;
        let flags = reader.read_u32()?;
        reader.ensure_count_fits(root_count, NODE_HEADER_SIZE)?;
        budget.ensure_nodes(root_count as usize)?;
        let mut roots = Vec::with_capacity(root_count as usize);
        for _ in 0..root_count {
            roots.push(decode_node(&mut reader, 0, &mut budget, &mut is_cancelled)?);
        }
        if reader.remaining() != 0 {
            return Err(CodecError::TrailingData);
        }

        Ok(Self {
            version,
            flags,
            roots,
        })
    }

    pub fn encode(&self) -> Result<Vec<u8>, CodecError> {
        let root_count = u32::try_from(self.roots.len()).map_err(|_| CodecError::LengthOverflow)?;
        let roots_length = self.roots.iter().try_fold(0usize, |total, root| {
            total
                .checked_add(node_length(root)?)
                .ok_or(CodecError::LengthOverflow)
        })?;
        let capacity = CAST_HEADER_SIZE
            .checked_add(roots_length)
            .ok_or(CodecError::LengthOverflow)?;
        let mut writer = Writer::with_capacity(capacity);
        writer.write_u32(CAST_MAGIC);
        writer.write_u32(self.version);
        writer.write_u32(root_count);
        writer.write_u32(self.flags);
        for root in &self.roots {
            encode_node(&mut writer, root)?;
        }
        Ok(writer.finish())
    }
}

fn decode_node(
    reader: &mut Reader<'_>,
    depth: usize,
    budget: &mut DecodeBudget,
    is_cancelled: &mut impl FnMut() -> bool,
) -> Result<CastNode, CodecError> {
    check_cancelled(is_cancelled)?;
    budget.add_node()?;
    if depth >= MAX_NODE_DEPTH {
        return Err(CodecError::NestingTooDeep);
    }
    let start = reader.position;
    let identifier = reader.read_u32()?;
    let node_size = reader.read_u32()?;
    if node_size < NODE_HEADER_SIZE as u32 {
        return Err(CodecError::InvalidNodeSize(node_size));
    }
    let expected_end = start
        .checked_add(node_size as usize)
        .filter(|end| *end <= reader.bytes.len())
        .ok_or(CodecError::InvalidNodeSize(node_size))?;
    let hash = reader.read_u64()?;
    let property_count = reader.read_u32()?;
    let child_count = reader.read_u32()?;
    reader.ensure_count_fits(property_count, PROPERTY_HEADER_SIZE)?;
    budget.add_properties(property_count as usize)?;
    let mut properties = Vec::with_capacity(property_count as usize);
    for _ in 0..property_count {
        properties.push(decode_property(reader, budget, is_cancelled)?);
    }

    reader.ensure_count_fits(child_count, NODE_HEADER_SIZE)?;
    budget.ensure_nodes(child_count as usize)?;
    let mut children = Vec::with_capacity(child_count as usize);
    for _ in 0..child_count {
        children.push(decode_node(reader, depth + 1, budget, is_cancelled)?);
    }
    if reader.position != expected_end {
        return Err(CodecError::InvalidNodeSize(node_size));
    }

    Ok(CastNode {
        identifier,
        hash,
        properties,
        children,
    })
}

fn decode_property(
    reader: &mut Reader<'_>,
    budget: &mut DecodeBudget,
    is_cancelled: &mut impl FnMut() -> bool,
) -> Result<CastProperty, CodecError> {
    check_cancelled(is_cancelled)?;
    let property_type = reader.take::<2>()?;
    let name_length = reader.read_u16()? as usize;
    let value_count = reader.read_u32()?;
    budget.add_text_bytes(name_length)?;
    let name = std::str::from_utf8(reader.read_bytes(name_length)?)
        .map_err(|_| CodecError::InvalidUtf8)?
        .to_owned();
    let count = usize::try_from(value_count).map_err(|_| CodecError::CountExceedsFile)?;
    let value_size = match property_type {
        [b'b', 0] => Some(1),
        [b'h', 0] => Some(2),
        [b'i', 0] | [b'f', 0] => Some(4),
        [b'l', 0] | [b'd', 0] | [b'2', b'v'] => Some(8),
        [b'3', b'v'] => Some(12),
        [b'4', b'v'] => Some(16),
        [b's', 0] => None,
        unsupported => return Err(CodecError::UnsupportedPropertyType(unsupported)),
    };
    if let Some(value_size) = value_size {
        reader.ensure_count_fits(value_count, value_size)?;
        budget.add_value_bytes(count.checked_mul(value_size).ok_or(
            CodecError::ResourceLimitExceeded(DecodeResource::ValueBytes),
        )?)?;
    }

    let values = match property_type {
        [b'b', 0] => {
            PropertyValues::Byte(reader.read_many(count, Reader::read_u8, is_cancelled)?)
        }
        [b'h', 0] => {
            PropertyValues::Short(reader.read_many(count, Reader::read_u16, is_cancelled)?)
        }
        [b'i', 0] => {
            PropertyValues::Integer32(reader.read_many(count, Reader::read_u32, is_cancelled)?)
        }
        [b'l', 0] => {
            PropertyValues::Integer64(reader.read_many(count, Reader::read_u64, is_cancelled)?)
        }
        [b'f', 0] => {
            PropertyValues::Float(reader.read_many(count, Reader::read_f32, is_cancelled)?)
        }
        [b'd', 0] => {
            PropertyValues::Double(reader.read_many(count, Reader::read_f64, is_cancelled)?)
        }
        [b's', 0] => {
            if value_count != 1 {
                return Err(CodecError::InvalidStringValueCount(value_count));
            }
            let remaining = budget.remaining_text_bytes();
            let (value, byte_count) = reader.read_null_terminated_utf8(remaining)?;
            budget.add_text_bytes(byte_count)?;
            PropertyValues::String(value)
        }
        [b'2', b'v'] => PropertyValues::Vector2(reader.read_many(
            count,
            |reader| Ok([reader.read_f32()?, reader.read_f32()?]),
            is_cancelled,
        )?),
        [b'3', b'v'] => PropertyValues::Vector3(reader.read_many(
            count,
            |reader| Ok([reader.read_f32()?, reader.read_f32()?, reader.read_f32()?]),
            is_cancelled,
        )?),
        [b'4', b'v'] => PropertyValues::Vector4(reader.read_many(
            count,
            |reader| {
                Ok([
                    reader.read_f32()?,
                    reader.read_f32()?,
                    reader.read_f32()?,
                    reader.read_f32()?,
                ])
            },
            is_cancelled,
        )?),
        unsupported => return Err(CodecError::UnsupportedPropertyType(unsupported)),
    };

    Ok(CastProperty { name, values })
}

struct DecodeBudget {
    limits: DecodeLimits,
    nodes: usize,
    properties: usize,
    value_bytes: usize,
    text_bytes: usize,
}

impl DecodeBudget {
    fn new(limits: DecodeLimits) -> Self {
        Self {
            limits,
            nodes: 0,
            properties: 0,
            value_bytes: 0,
            text_bytes: 0,
        }
    }

    fn ensure_nodes(&self, additional: usize) -> Result<(), CodecError> {
        ensure_budget(
            self.nodes,
            additional,
            self.limits.max_nodes,
            DecodeResource::Nodes,
        )
    }

    fn add_node(&mut self) -> Result<(), CodecError> {
        self.ensure_nodes(1)?;
        self.nodes += 1;
        Ok(())
    }

    fn add_properties(&mut self, additional: usize) -> Result<(), CodecError> {
        ensure_budget(
            self.properties,
            additional,
            self.limits.max_properties,
            DecodeResource::Properties,
        )?;
        self.properties += additional;
        Ok(())
    }

    fn add_value_bytes(&mut self, additional: usize) -> Result<(), CodecError> {
        ensure_budget(
            self.value_bytes,
            additional,
            self.limits.max_value_bytes,
            DecodeResource::ValueBytes,
        )?;
        self.value_bytes += additional;
        Ok(())
    }

    fn remaining_text_bytes(&self) -> usize {
        self.limits.max_text_bytes.saturating_sub(self.text_bytes)
    }

    fn add_text_bytes(&mut self, additional: usize) -> Result<(), CodecError> {
        ensure_budget(
            self.text_bytes,
            additional,
            self.limits.max_text_bytes,
            DecodeResource::TextBytes,
        )?;
        self.text_bytes += additional;
        Ok(())
    }
}

fn ensure_budget(
    current: usize,
    additional: usize,
    maximum: usize,
    resource: DecodeResource,
) -> Result<(), CodecError> {
    if current
        .checked_add(additional)
        .is_none_or(|total| total > maximum)
    {
        Err(CodecError::ResourceLimitExceeded(resource))
    } else {
        Ok(())
    }
}

fn encode_node(writer: &mut Writer, node: &CastNode) -> Result<(), CodecError> {
    writer.write_u32(node.identifier);
    writer.write_u32(u32::try_from(node_length(node)?).map_err(|_| CodecError::LengthOverflow)?);
    writer.write_u64(node.hash);
    writer.write_u32(u32::try_from(node.properties.len()).map_err(|_| CodecError::LengthOverflow)?);
    writer.write_u32(u32::try_from(node.children.len()).map_err(|_| CodecError::LengthOverflow)?);
    for property in &node.properties {
        encode_property(writer, property)?;
    }
    for child in &node.children {
        encode_node(writer, child)?;
    }
    Ok(())
}

fn encode_property(writer: &mut Writer, property: &CastProperty) -> Result<(), CodecError> {
    let name = property.name.as_bytes();
    writer.write_bytes(property.values.property_type());
    writer.write_u16(u16::try_from(name.len()).map_err(|_| CodecError::LengthOverflow)?);
    writer.write_u32(u32::try_from(property.values.len()).map_err(|_| CodecError::LengthOverflow)?);
    writer.write_bytes(name);
    property.values.encode(writer);
    Ok(())
}

fn node_length(node: &CastNode) -> Result<usize, CodecError> {
    let property_length = node.properties.iter().try_fold(0usize, |total, property| {
        total
            .checked_add(property_length(property)?)
            .ok_or(CodecError::LengthOverflow)
    })?;
    let child_length = node.children.iter().try_fold(0usize, |total, child| {
        total
            .checked_add(node_length(child)?)
            .ok_or(CodecError::LengthOverflow)
    })?;
    NODE_HEADER_SIZE
        .checked_add(property_length)
        .and_then(|total| total.checked_add(child_length))
        .ok_or(CodecError::LengthOverflow)
}

fn property_length(property: &CastProperty) -> Result<usize, CodecError> {
    let value_length = property.values.byte_len()?;
    PROPERTY_HEADER_SIZE
        .checked_add(property.name.len())
        .and_then(|total| total.checked_add(value_length))
        .ok_or(CodecError::LengthOverflow)
}

impl PropertyValues {
    fn property_type(&self) -> &'static [u8; 2] {
        match self {
            Self::Byte(_) => b"b\0",
            Self::Short(_) => b"h\0",
            Self::Integer32(_) => b"i\0",
            Self::Integer64(_) => b"l\0",
            Self::Float(_) => b"f\0",
            Self::Double(_) => b"d\0",
            Self::String(_) => b"s\0",
            Self::Vector2(_) => b"2v",
            Self::Vector3(_) => b"3v",
            Self::Vector4(_) => b"4v",
        }
    }

    fn len(&self) -> usize {
        match self {
            Self::Byte(values) => values.len(),
            Self::Short(values) => values.len(),
            Self::Integer32(values) => values.len(),
            Self::Integer64(values) => values.len(),
            Self::Float(values) => values.len(),
            Self::Double(values) => values.len(),
            Self::String(_) => 1,
            Self::Vector2(values) => values.len(),
            Self::Vector3(values) => values.len(),
            Self::Vector4(values) => values.len(),
        }
    }

    fn byte_len(&self) -> Result<usize, CodecError> {
        match self {
            Self::Byte(values) => Some(values.len()),
            Self::Short(values) => values.len().checked_mul(2),
            Self::Integer32(values) => values.len().checked_mul(4),
            Self::Integer64(values) => values.len().checked_mul(8),
            Self::Float(values) => values.len().checked_mul(4),
            Self::Double(values) => values.len().checked_mul(8),
            Self::String(value) => value.len().checked_add(1),
            Self::Vector2(values) => values.len().checked_mul(8),
            Self::Vector3(values) => values.len().checked_mul(12),
            Self::Vector4(values) => values.len().checked_mul(16),
        }
        .ok_or(CodecError::LengthOverflow)
    }

    fn encode(&self, writer: &mut Writer) {
        match self {
            Self::Byte(values) => writer.write_bytes(values),
            Self::Short(values) => values.iter().for_each(|value| writer.write_u16(*value)),
            Self::Integer32(values) => values.iter().for_each(|value| writer.write_u32(*value)),
            Self::Integer64(values) => values.iter().for_each(|value| writer.write_u64(*value)),
            Self::Float(values) => values.iter().for_each(|value| writer.write_f32(*value)),
            Self::Double(values) => values.iter().for_each(|value| writer.write_f64(*value)),
            Self::String(value) => {
                writer.write_bytes(value.as_bytes());
                writer.write_u8(0);
            }
            Self::Vector2(values) => values
                .iter()
                .flatten()
                .for_each(|value| writer.write_f32(*value)),
            Self::Vector3(values) => values
                .iter()
                .flatten()
                .for_each(|value| writer.write_f32(*value)),
            Self::Vector4(values) => values
                .iter()
                .flatten()
                .for_each(|value| writer.write_f32(*value)),
        }
    }
}

struct Reader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, position: 0 }
    }

    fn remaining(&self) -> usize {
        self.bytes.len().saturating_sub(self.position)
    }

    fn ensure_count_fits(&self, count: u32, minimum_item_size: usize) -> Result<(), CodecError> {
        let count = usize::try_from(count).map_err(|_| CodecError::CountExceedsFile)?;
        let required = count
            .checked_mul(minimum_item_size)
            .ok_or(CodecError::CountExceedsFile)?;
        if required > self.remaining() {
            return Err(CodecError::CountExceedsFile);
        }
        Ok(())
    }

    fn read_many<T>(
        &mut self,
        count: usize,
        mut read: impl FnMut(&mut Self) -> Result<T, CodecError>,
        is_cancelled: &mut impl FnMut() -> bool,
    ) -> Result<Vec<T>, CodecError> {
        if count > self.remaining() {
            return Err(CodecError::CountExceedsFile);
        }
        let mut values = Vec::with_capacity(count);
        for index in 0..count {
            if index % 4_096 == 0 {
                check_cancelled(is_cancelled)?;
            }
            values.push(read(self)?);
        }
        Ok(values)
    }

    fn read_u8(&mut self) -> Result<u8, CodecError> {
        Ok(self.take::<1>()?[0])
    }

    fn read_u16(&mut self) -> Result<u16, CodecError> {
        Ok(u16::from_le_bytes(self.take::<2>()?))
    }

    fn read_u32(&mut self) -> Result<u32, CodecError> {
        Ok(u32::from_le_bytes(self.take::<4>()?))
    }

    fn read_u64(&mut self) -> Result<u64, CodecError> {
        Ok(u64::from_le_bytes(self.take::<8>()?))
    }

    fn read_f32(&mut self) -> Result<f32, CodecError> {
        Ok(f32::from_le_bytes(self.take::<4>()?))
    }

    fn read_f64(&mut self) -> Result<f64, CodecError> {
        Ok(f64::from_le_bytes(self.take::<8>()?))
    }

    fn read_bytes(&mut self, length: usize) -> Result<&'a [u8], CodecError> {
        let end = self
            .position
            .checked_add(length)
            .ok_or(CodecError::UnexpectedEndOfFile)?;
        let slice = self
            .bytes
            .get(self.position..end)
            .ok_or(CodecError::UnexpectedEndOfFile)?;
        self.position = end;
        Ok(slice)
    }

    fn read_null_terminated_utf8(
        &mut self,
        maximum_length: usize,
    ) -> Result<(String, usize), CodecError> {
        let tail = self
            .bytes
            .get(self.position..)
            .ok_or(CodecError::UnexpectedEndOfFile)?;
        let terminator = tail
            .iter()
            .position(|byte| *byte == 0)
            .ok_or(CodecError::UnexpectedEndOfFile)?;
        if terminator > maximum_length {
            return Err(CodecError::ResourceLimitExceeded(DecodeResource::TextBytes));
        }
        let value = std::str::from_utf8(&tail[..terminator])
            .map_err(|_| CodecError::InvalidUtf8)?
            .to_owned();
        self.position += terminator + 1;
        Ok((value, terminator))
    }

    fn take<const LENGTH: usize>(&mut self) -> Result<[u8; LENGTH], CodecError> {
        Ok(self
            .read_bytes(LENGTH)?
            .try_into()
            .expect("slice length was checked"))
    }
}

fn check_cancelled(is_cancelled: &mut impl FnMut() -> bool) -> Result<(), CodecError> {
    if is_cancelled() {
        Err(CodecError::Cancelled)
    } else {
        Ok(())
    }
}

struct Writer {
    bytes: Vec<u8>,
}

impl Writer {
    fn with_capacity(capacity: usize) -> Self {
        Self {
            bytes: Vec::with_capacity(capacity),
        }
    }

    fn write_u8(&mut self, value: u8) {
        self.bytes.push(value);
    }

    fn write_u16(&mut self, value: u16) {
        self.write_bytes(&value.to_le_bytes());
    }

    fn write_u32(&mut self, value: u32) {
        self.write_bytes(&value.to_le_bytes());
    }

    fn write_u64(&mut self, value: u64) {
        self.write_bytes(&value.to_le_bytes());
    }

    fn write_f32(&mut self, value: f32) {
        self.write_bytes(&value.to_le_bytes());
    }

    fn write_f64(&mut self, value: f64) {
        self.write_bytes(&value.to_le_bytes());
    }

    fn write_bytes(&mut self, value: &[u8]) {
        self.bytes.extend_from_slice(value);
    }

    fn finish(self) -> Vec<u8> {
        self.bytes
    }
}
