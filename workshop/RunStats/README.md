# RunStats Workshop workspace

This directory is the Steam Workshop upload workspace for public RunStats item `3797791393`.

- `workshop.json` contains the Steam title, description, visibility, and release note.
- `image.png` is the required Workshop thumbnail and must remain below 1 MB.
- `content` is generated from the verified Release build by `tools/Package-RunStatsWorkshop.ps1`.
- `mod_id.txt` contains Workshop item ID `3797791393`. Preserve it so future uploads update this item instead of creating a duplicate.
- `rollback/v0.1.0` preserves the three files from the currently published release. It is not part of the uploader content path.

The first upload was reviewed privately before the item was updated to `"visibility": "public"`. Future uploads use the same `mod_id.txt` and update this item.

The staged `content` directory is RunStats v0.2.0. It contains exactly the DLL, PCK, and manifest and must be reviewed before any separately authorized upload.
