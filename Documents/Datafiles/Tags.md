# Tags
The tag database is located in `src\StarXelem\datafiles\libs\foundry\records\tagdatabase\tagdatabase.tagdatabase.xml`.

## Structure
Tags are stored as a hierarchical tree of `Tag` elements.
- **Root**: The file contains a root set of tags.
- **Tag Properties**: Each tag has a `RecordId`, `RecordName`, and a human-readable `tagName`.
- **Hierarchy**: Tags can have `children` (also `Tag` elements), creating a parent-child relationship (e.g., `Global` -> `Race` -> `Human`).
