# 3D Body layer pair

Requested behavior: generated online models belong to a typed Top/Bottom 3D Body pair instead of Mechanical 1. Keep legacy AD compatibility and the existing shared-core/adapter installer.

Author Mechanical 6/7 with 3DBodyTop/3DBodyBottom types in the V9 cache and legacy V8/layer dictionaries, plus MECHPAIR0. These layer numbers fit legacy mechanical layers and are unused by the converter's non-body mapping. AD can map by type to a destination PCB's own layer numbers.

New conversion assigns only ComponentBody objects. Aggregate rebuild normalizes old downloaded bodies and reads existing types/pairs to preserve bottom bodies. Keep geometry, pads, STEP resources, model transformations and source download hashes. Increment owned total signatures so global and project totals rebuild through existing transactional machinery. Do not directly change placed PCB components or user layer definitions.

Verify raw parameter fields independently, body layer roundtrip, legacy source migration and repeated project merge, source immutability, existing converter tests, real cache snapshots through each managed runtime, immutable adapter ABI and isolated installer. No AD automation or visible GUI tests. Actual destination type mapping, component flip and 3D view remain user acceptance.
