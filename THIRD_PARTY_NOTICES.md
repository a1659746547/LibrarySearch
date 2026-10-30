# Third-party notices

LibrarySearch original code is licensed under MIT. The following vendored projects retain their original copyright and licenses:

- **easyeda2altium**, revision `ad21515ef0a7dd65cf0462458dc1ba8e8f47ac6d`, MIT OR Apache-2.0. Original license texts remain in its source directory and `plugin/licenses/easyeda2altium/`.
- **altiumrs**, https://github.com/issus/altiumrs, revision `2b45c1f91278680255d1f8dac067b95e0917f143`, MIT OR Apache-2.0. Original license texts remain in its source directory and `plugin/licenses/altiumrs/`.
- Rust dependency notices and available license texts are retained under `plugin/licenses/rust-dependencies/`.

Local changes include exact pin endpoints and visibility, schematic appearance, SVG geometry, footprint pads/slots and text, embedded STEP models, source caching and segmented local indexing. Both native executables are built from the included source. Compatibility DLL snapshots are LibrarySearch build artifacts retained to verify the existing adapter ABI; they are not Altium SDK assemblies.

Altium Designer and its SDK are not redistributed. The plugin references interfaces supplied by a user's licensed installation. LibrarySearch is not an official Altium or JLCEDA product.

CAD data is fetched on demand from the official JLCEDA/EasyEDA library. The repository and software release do not include a supplier component database, developer cache or user projects. Software licenses do not grant rights to redistribute third-party CAD data.
