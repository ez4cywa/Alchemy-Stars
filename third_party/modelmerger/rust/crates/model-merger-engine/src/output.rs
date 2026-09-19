//! Publish a validated temporary file without replacing a user's existing output.
use std::{io, path::Path};

pub(crate) fn publish_new(temporary: &Path, output: &Path) -> io::Result<()> {
    publish_with_link(temporary, output, |from, to| std::fs::hard_link(from, to))
}

fn publish_with_link(
    temporary: &Path,
    output: &Path,
    link: impl FnOnce(&Path, &Path) -> io::Result<()>,
) -> io::Result<()> {
    match link(temporary, output) {
        Ok(()) => Ok(()),
        #[cfg(windows)]
        Err(error)
            if error.kind() == io::ErrorKind::Unsupported
                || matches!(error.raw_os_error(), Some(1 | 50)) =>
        {
            move_without_replacing(temporary, output)
        }
        Err(error) => Err(error),
    }
}

#[cfg(windows)]
fn move_without_replacing(temporary: &Path, output: &Path) -> io::Result<()> {
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Storage::FileSystem::{MOVEFILE_WRITE_THROUGH, MoveFileExW};
    fn wide(path: &Path) -> io::Result<Vec<u16>> {
        let mut value: Vec<_> = path.as_os_str().encode_wide().collect();
        if value.contains(&0) {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "Path contains a null character",
            ));
        }
        value.push(0);
        Ok(value)
    }
    let from = wide(temporary)?;
    let to = wide(output)?;
    // Both buffers are valid, null-terminated UTF-16 and live through this call.
    // Omitting MOVEFILE_REPLACE_EXISTING preserves the no-overwrite guarantee.
    // The validated temporary file is in the output directory, so this is a same-volume move.
    if unsafe { MoveFileExW(from.as_ptr(), to.as_ptr(), MOVEFILE_WRITE_THROUGH) } == 0 {
        Err(io::Error::last_os_error())
    } else {
        Ok(())
    }
}

#[cfg(all(test, windows))]
mod tests {
    use super::*;
    #[test]
    fn unsupported_hard_links_still_publish_without_overwriting() {
        let directory = std::env::temp_dir().join(format!("cast-publish-{}", std::process::id()));
        std::fs::create_dir_all(&directory).unwrap();
        let temporary = directory.join("new.cast");
        let output = directory.join("saved.cast");
        std::fs::write(&temporary, b"validated model").unwrap();
        publish_with_link(&temporary, &output, |_, _| {
            Err(io::Error::from_raw_os_error(1))
        })
        .unwrap();
        assert_eq!(std::fs::read(&output).unwrap(), b"validated model");
        std::fs::write(&temporary, b"another model").unwrap();
        assert!(
            publish_with_link(&temporary, &output, |_, _| Err(
                io::Error::from_raw_os_error(1)
            ))
            .is_err()
        );
        assert_eq!(std::fs::read(&output).unwrap(), b"validated model");
        assert_eq!(std::fs::read(&temporary).unwrap(), b"another model");
        std::fs::remove_file(&temporary).unwrap();
        std::fs::remove_file(&output).unwrap();
        std::fs::remove_dir(&directory).unwrap();
    }
}
