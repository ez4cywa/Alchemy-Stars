use model_merger_engine::{
    NoopObserver,
    ammunition::{self, FillRequest},
};
use std::path::PathBuf;
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args_os().skip(1).collect();
    if args.len() < 3 {
        return Err(
            "Usage: fill_magazine WEAPON.cast AMMO.cast OUTPUT.cast [MAGAZINE_OR_BONE | SOURCE:SPARE ...]".into(),
        );
    }
    let weapon = PathBuf::from(&args[0]);
    let analysis = ammunition::inspect(&weapon, &NoopObserver)?;
    println!("{analysis:#?}");
    let names: Vec<String> = if args.len() > 3 {
        args[3..]
            .iter()
            .map(|s| s.to_string_lossy().into_owned())
            .collect()
    } else {
        analysis
            .magazines
            .first()
            .map(|m| vec![m.name.clone()])
            .unwrap_or_default()
    };
    let (copies, names): (Vec<_>, Vec<_>) = names.into_iter().partition(|name| name.contains(':'));
    let replicas = copies
        .into_iter()
        .map(|copy| {
            let (source, target) = copy.split_once(':').unwrap();
            ammunition::MagazineReplica {
                source: source.into(),
                target: target.into(),
            }
        })
        .collect();
    let (extra_slots, magazines) = names
        .into_iter()
        .partition(|name| analysis.excluded_slots.contains(name));
    let result = ammunition::fill(
        FillRequest {
            weapon,
            ammunition: PathBuf::from(&args[1]),
            output: PathBuf::from(&args[2]),
            magazines,
            extra_slots,
            replicas,
        },
        &NoopObserver,
    )?;
    println!("{result:#?}");
    Ok(())
}
