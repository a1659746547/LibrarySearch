# Third-party notices

LibrarySearch original code is licensed under MIT. The following vendored projects retain their original copyright and licenses:

- **easyeda2altium**, https://github.com/korbin/easyeda2altium, revision `ad21515ef0a7dd65cf0462458dc1ba8e8f47ac6d`, MIT OR Apache-2.0. Original license texts remain in its source directory and `plugin/licenses/easyeda2altium/`.
- **altiumrs**, actual source snapshot https://github.com/korbin/altiumrs (upstream workspace metadata: https://github.com/issus/altiumrs), revision `2b45c1f91278680255d1f8dac067b95e0917f143`, MIT OR Apache-2.0. Original license texts remain in its source directory and `plugin/licenses/altiumrs/`.
- Rust dependency notices and available license texts are retained under `plugin/licenses/rust-dependencies/`.

Local changes include exact pin endpoints and visibility, schematic appearance, SVG geometry, footprint pads/slots and text, embedded STEP models, source caching and segmented local indexing. Both native executables are built from the included source. Compatibility DLL snapshots are LibrarySearch build artifacts retained to verify the existing adapter ABI; they are not Altium SDK assemblies.

Altium Designer and its SDK are not redistributed. The plugin references interfaces supplied by a user's licensed installation. LibrarySearch is not an official Altium or JLCEDA product.

CAD data is fetched on demand from the official JLCEDA/EasyEDA library. The repository and software release do not include a supplier component database, developer cache or user projects. Software licenses do not grant rights to redistribute third-party CAD data.

## Search, acquisition and format references

The online acquisition/conversion path builds on the modified easyeda2altium snapshot above. The local library scanner reads SchLib, PcbLib and IntLib through the modified altiumrs snapshot. `plugin/src/ModelSearch.cs` uses the JLCEDA product-search service; data-service attribution is distinct from software provenance.

The EasyEDA parser's source header explicitly names **easyeda2kicad/easyeda/easyeda_importer.py**, from https://github.com/uPesy/easyeda2kicad.py, as its shape/pin format reference. That Python project is not bundled as a dependency; its own repository retains its license and notices. This acknowledgement does not replace or relicense any upstream work.

For the source-history evidence, actual fork origins and revision IDs, see [SOURCE_ORIGINS.md](SOURCE_ORIGINS.md).
