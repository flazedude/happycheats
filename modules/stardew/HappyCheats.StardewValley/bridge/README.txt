HappyCheats Stardew Valley bridge, schema 1.
The mod writes status.json, reads commands/<request GUID>.json and writes
responses/<request GUID>.json. Commands are scoped to one running process and
loaded save session, expire after 30 seconds and are never replayed automatically.
Do not copy runtime status, commands or responses between game installations.
