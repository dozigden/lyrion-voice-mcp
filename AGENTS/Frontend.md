# Frontend Guidance

- Use Vue 3, TypeScript, Vite, Pinia, and Vue Router.
- Organise feature code beneath `src/features/<feature>`; use `src/shared` only for genuinely cross-feature primitives.
- Keep API wrappers strongly typed and scoped to their route namespace.
- Components own local form/draft state; Pinia stores own shared feature state and asynchronous operations.
- Treat backend contracts as authoritative. Do not add speculative client normalisation without an observed failure mode.
- Keep shared/global CSS in `src/style.css` or `src/shared/styles`; keep page/component-specific CSS in scoped Vue styles.
- Prefer semantic HTML, labelled controls, keyboard access, and responsive layouts.
- Do not use nested ternaries or `$event` expressions in templates; use named handlers and explicit branching.
- Keep operational warnings concise. The trusted-LAN warning must remain visible wherever connection details are presented.
- Search-observation pages must keep retention/privacy visible and describe retrieval sources generically; observations may combine the production catalogue resolver with LMS playlist retrieval. Display a candidate's persisted winning match signal when present, translating its stable underscore-separated value into a compact human-readable label without reinterpreting it.
- Keep `/licences` backed by the committed static `/third-party-licenses` mirror so disclosure remains available without the API or an external network request.
