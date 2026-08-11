# C# Coding Conventions

- Target .NET 10 with nullable reference types and implicit usings enabled.
- Prefer British English unless matching an external protocol field or command.
- Prefer constructor injection and small sealed implementation classes.
- Propagate `CancellationToken` through asynchronous boundaries.
- Prefer return values over `out` parameters unless an external API or measured performance requirement justifies them.
- Keep helper names aligned with their behaviour; do not hide mutation or I/O behind parsing or validation names.
- Do not use nested ternary expressions. Use explicit `if`, `switch`, or a small helper.
- Keep endpoint and MCP transport code free of business decisions.
- Treat LMS JSON as an external contract: parse explicitly, tolerate documented LMS coercions, and fail clearly on invalid response-level shapes.
- Tests should normally contain one clear Arrange/Act/Assert flow.

