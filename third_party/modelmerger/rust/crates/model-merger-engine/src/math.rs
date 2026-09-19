use std::ops::{Add, Sub};

#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub(crate) struct Vec3(pub f32, pub f32, pub f32);

impl Add for Vec3 {
    type Output = Self;

    fn add(self, right: Self) -> Self::Output {
        Self(self.0 + right.0, self.1 + right.1, self.2 + right.2)
    }
}

impl Sub for Vec3 {
    type Output = Self;

    fn sub(self, right: Self) -> Self::Output {
        Self(self.0 - right.0, self.1 - right.1, self.2 - right.2)
    }
}

impl From<[f32; 3]> for Vec3 {
    fn from(value: [f32; 3]) -> Self {
        Self(value[0], value[1], value[2])
    }
}

impl From<Vec3> for [f32; 3] {
    fn from(value: Vec3) -> Self {
        [value.0, value.1, value.2]
    }
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub(crate) struct Quaternion(pub f32, pub f32, pub f32, pub f32);

impl Quaternion {
    pub(crate) const IDENTITY: Self = Self(0.0, 0.0, 0.0, 1.0);

    pub(crate) fn inverse(self) -> Self {
        Self(-self.0, -self.1, -self.2, self.3)
    }

    pub(crate) fn multiply(self, right: Self) -> Self {
        Self(
            self.3 * right.0 + self.0 * right.3 + self.1 * right.2 - self.2 * right.1,
            self.3 * right.1 + self.1 * right.3 + self.2 * right.0 - self.0 * right.2,
            self.3 * right.2 + self.2 * right.3 + self.0 * right.1 - self.1 * right.0,
            self.3 * right.3 - self.0 * right.0 - self.1 * right.1 - self.2 * right.2,
        )
    }

    pub(crate) fn rotate(self, vector: Vec3) -> Vec3 {
        let xx = self.0 * self.0;
        let yy = self.1 * self.1;
        let zz = self.2 * self.2;
        let xy = self.0 * self.1;
        let xz = self.0 * self.2;
        let xw = self.0 * self.3;
        let yz = self.1 * self.2;
        let yw = self.1 * self.3;
        let zw = self.2 * self.3;
        Vec3(
            vector.0 * (1.0 - 2.0 * (yy + zz))
                + vector.1 * (2.0 * (xy - zw))
                + vector.2 * (2.0 * (xz + yw)),
            vector.0 * (2.0 * (xy + zw))
                + vector.1 * (1.0 - 2.0 * (xx + zz))
                + vector.2 * (2.0 * (yz - xw)),
            vector.0 * (2.0 * (xz - yw))
                + vector.1 * (2.0 * (yz + xw))
                + vector.2 * (1.0 - 2.0 * (xx + yy)),
        )
    }
}

impl Default for Quaternion {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl From<[f32; 4]> for Quaternion {
    fn from(value: [f32; 4]) -> Self {
        Self(value[0], value[1], value[2], value[3])
    }
}

impl From<Quaternion> for [f32; 4] {
    fn from(value: Quaternion) -> Self {
        [value.0, value.1, value.2, value.3]
    }
}

#[cfg(test)]
mod tests {
    use super::{Quaternion, Vec3};

    #[test]
    fn identity_rotation_preserves_vector() {
        assert_eq!(
            Vec3(1.0, 2.0, 3.0),
            Quaternion::IDENTITY.rotate(Vec3(1.0, 2.0, 3.0))
        );
    }

    #[test]
    fn inverse_cancels_unit_quaternion() {
        let rotation = Quaternion(0.0, 0.0, 0.70710677, 0.70710677);
        let result = rotation.multiply(rotation.inverse());
        assert!((result.0).abs() < 0.00001);
        assert!((result.1).abs() < 0.00001);
        assert!((result.2).abs() < 0.00001);
        assert!((result.3 - 1.0).abs() < 0.00001);
    }
}
