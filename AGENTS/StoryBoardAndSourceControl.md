# Story Board and Source Control Guidance

## BoardOil

- Default board: Lyrion MCP (`boardId: 12`).
- Columns: Todo `46`, In Progress `47`, Done `48`.
- Treat card order within Todo as intended execution order unless the user reprioritises it.
- Use direct BoardOil MCP operations.
- Before implementation, read the card and move it to In Progress.
- Record an agreed implementation plan in the card when useful.
- Move a card to Done only after the user confirms completion.
- A card update is full-state: read the card first and preserve its title, card type, tags, slick, and external URL.
- In user-facing references to cards, prefix the card number and title with the card type's emoji and append any tag emojis in brackets, for example `📘 #47 Search albums by release year (🔎)`. Use the emojis supplied by BoardOil and omit missing emojis or empty brackets rather than inventing replacements.

## Source control

- Work on the current branch. Do not create or switch branches or open pull requests unless explicitly requested.
- Do not commit or push until the user has reviewed the work and requested the action.
- Keep unrelated changes out of story commits.
- When working from a card, prefix its commit message with the card number, for example `#7 Add the application skeleton.`
- Do not commit `.codex`, `.data`, build outputs, test results, local LMS settings, or secrets.

