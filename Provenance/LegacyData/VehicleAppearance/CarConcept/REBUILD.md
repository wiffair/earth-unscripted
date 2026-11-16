# Rebuilding the appearance

The included CarConcept-RoadRush.glb is the reviewed, logo-free derivative, not the original branded source. Source digest and changes are recorded in its provenance JSON; author licence and credits are included.

The Unity Editor menu/CLI method `RoadRush.Editor.VehicleAppearanceImporter.Run` reads this project-local derivative first, checks its pinned SHA-256 and generates fresh meshes/materials plus the Resources prefab. `VehicleAppearanceVerification.Run` verifies the imported asset and real texture packing. The project builds normally from the already generated Assets without running this importer. Importing an unrelated model is not yet a supported player feature.

To reproduce the derivative from the original author's source, use tools/prepare_car_concept.py with the exact source hash/revision from provenance. Keep the original logo-bearing source outside distributions; preserve CC BY4.0 credits and the change notice. No account or online service is needed to rebuild from the included derivative.
