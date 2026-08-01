# Modifications

OmniBlock is derived from BetaSharp. Beginning with the first entry below, these changes were made to the upstream code under CPAL-1.0. This file documents the date, author, description, and original pre-rewrite commit identifier for each change.

- 2026-07-31 — feat(network): add the ITransport seam and a LiteNetLib implementation (Nicolas Vyčas Nery; `6c2a02eb0fecc186aa10dbdbf30c10916b4081cc`)
- 2026-08-01 — feat(network): move multiplayer onto UDP (Nicolas Vyčas Nery; `d79c5fc20d0dbb3c3cbc867b8428e5991229acd8`)
- 2026-08-01 — perf(network): stop paying LiteNetLib's default send pacing (Nicolas Vyčas Nery; `a13bc22bed0d3c53b26baecf487f92d60c7e2294`)
- 2026-08-01 — feat(network): adapt the interpolation delay to jitter and to starvation (Nicolas Vyčas Nery; `fa6e1f3990c28d2040e4b195bdd65c84f4819f4c`)
- 2026-08-01 — fix(network): do not credit starvation to an entity that merely stopped moving (Nicolas Vyčas Nery; `ba1010dd4e61033e722fb15dcf0451fbec87f157`)
- 2026-08-01 — feat(network): add a palette-based chunk wire encoding (Nicolas Vyčas Nery; `d175f594f766d7929a06136aec238a32e023d929`)
- 2026-08-01 — feat(network): send chunks in the palette encoding (Nicolas Vyčas Nery; `4153cb2e2a9dd385c7dc6a1afe2b8d083602d0fb`)
- 2026-08-01 — feat(network): skip sending chunks the client already has (Nicolas Vyčas Nery; `02d2489e8efc50854ed9e3f0a1c9b4150829bda3`)
- 2026-08-01 — fix(network): offer the chunk cache before the server sends chunks (Nicolas Vyčas Nery; `943b4005d0e5d8b4171d7f7fcd2543a91d453aa2`)
- 2026-08-01 — fix(client): release the connection's resources on every disconnect path (Nicolas Vyčas Nery; `27fbf6fbad4a58df217de52a815e79ad3d30558b`)
- 2026-08-01 — fix(network): advertise the whole chunk cache, not a fixed radius around the player (Nicolas Vyčas Nery; `348a3563ad9eafc758bc19a430bd2309cf105f39`)
- 2026-08-01 — fix(client): report the wire saving the chunk cache achieved, not the blob size (Nicolas Vyčas Nery; `c3e646803678ff5dcd7ecb20b6ff5f9d53083011`)
