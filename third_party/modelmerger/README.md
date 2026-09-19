# ModelMerger native integration

Vendored from https://github.com/ez4cywa/ModelMergerGUI at commit
`ac0bfeb577651c34b3de4fdbafa4b1f615e5f84f` (workspace version 2.2.2).
`rust/crates/cast-codec`, `rust/crates/model-merger-engine`, and
`tests/fixtures/rust-migration` are upstream sources/fixtures. The only core
integration patch is `PreparedMerge::with_overwrite(bool)` in engine `src/lib.rs`:
it sets the existing overwrite flag after preparation, without rereading inputs
or changing the merge algorithm. All other core files are unmodified.
Upstream MIT license is retained in `LICENSE`. No upstream GUI or font assets
are included. `alchemy-model-merger` is the Alchemy Stars JSONL adapter.

Build: `cargo build --manifest-path third_party/modelmerger/rust/Cargo.toml --release --locked`

Verify: `cargo test --manifest-path third_party/modelmerger/rust/Cargo.toml --locked`

The executable is `rust/target/release/alchemy-model-merger.exe` on Windows.
One process handles one task. Send a JSON command followed by a newline on
stdin; read flushed JSON events from stdout. Merge emits `prepared` with the
resolved output path and waits for `execute` so the host can claim that path.
`cancel` requests cooperative cancellation; stdin closure also cancels unfinished
work. Do not terminate processes during writes. Outputs use upstream temporary
write/readback/atomic commit. Ammunition filling never replaces an existing file.

Commands: `merge`, `inspect_ammunition`, `fill_ammunition`, `preview`; control commands:
`execute`, `cancel`. Events: `progress`, `prepared`, `analysis`, `completed`,
`error`. Errors include an additive `code` field (`cancelled`, `protocol`,
`OutputAlreadyExists`, other engine validation codes, or `engine`). Protocol
errors terminate with exit status 1. Input JSON lines are limited to 1 MiB.
Preview responses contain upstream mesh positions, normals, u32 triangle indices,
bounds, original geometry statistics, and sampled geometry statistics. The
triangle limit defaults to 250,000 and is capped at that value; previews are
read-only. Response lines may exceed 1 MiB because they contain geometry.

For overwrite confirmation after automatic root selection, send `merge` with
`overwrite:true`; `prepared` also reports `output_exists`. This grants permission
to prepare only: the host must resolve its output claim and ask the user before
sending `{ "command":"execute", "overwrite":true }` if the output exists.
Execute defaults to `overwrite:false` regardless of the preparation flag, so a
newly appearing output is also protected by upstream's final no-replace commit.
Cancel leaves existing output unchanged.

The integration test suite exercises the actual executable protocol, preparation
barrier, cancellation/EOF cleanup, overwrite preservation, bounded preview,
invalid commands, and ammunition inspection/fill/replication/no-overwrite. The
unmodified upstream codec/merge/preview/ammunition tests run in the same workspace.

Generate small synthetic ammunition fixtures for host smoke tests into a new
directory (existing files are rejected):

```powershell
cargo run --manifest-path third_party/modelmerger/rust/Cargo.toml --locked -p alchemy-model-merger --example generate_ammunition_fixture -- output/model-merger-ammo-fixture
```

`weapon.cast` contains `j_mag1` with slot `j_ammo_01`, spare `j_mag2`, and excluded
slot `j_ammo_99`. `ammo.cast` has a single rigid `tag_ammo` bone and mesh. Selecting
`j_mag1`, extra `j_ammo_99`, and replica `j_mag1` to `j_mag2` inserts three meshes.
