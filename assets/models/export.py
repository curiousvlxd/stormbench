import sys
from pathlib import Path

import bpy


def export_collection(collection: bpy.types.Collection, out_dir: Path) -> Path:
    """Write one top-level collection as a GLB, moved from its spot in the file to the origin."""
    offset = collection.instance_offset.copy()
    roots = [o for o in collection.all_objects if o.parent is None]
    for obj in roots:
        obj.location -= offset
    view_layer = bpy.context.view_layer
    view_layer.active_layer_collection = view_layer.layer_collection.children[collection.name]
    path = out_dir / f"{collection.name}.glb"
    bpy.ops.export_scene.gltf(
        filepath=str(path),
        export_format="GLB",
        use_active_collection=True,
        use_active_collection_with_nested=True,
        export_extras=True,
        export_yup=True,
        export_apply=True,
        export_normals=True,
        export_materials="EXPORT",
    )
    for obj in roots:
        obj.location += offset
    return path


def main() -> None:
    """Export every top-level collection of the open .blend into the directory after '--'."""
    out_dir = Path(sys.argv[sys.argv.index("--") + 1])
    out_dir.mkdir(parents=True, exist_ok=True)
    for collection in bpy.context.scene.collection.children:
        print("exported", export_collection(collection, out_dir))


main()
