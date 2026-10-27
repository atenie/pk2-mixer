# Spec 03 — Localization (.lng)

Status: **verified** against `default2.lng` (2,822 entries).
Implemented in `src/assets/lng.cpp`.

## Format

A `.lng` file is a plain-text file, CRLF-terminated. From the file's own header
comments:

- Lines beginning with `;` are comments.
- Strings begin and end with a double quote `"`.
- `\n` denotes a line break.
- `%1`, `%2`, … are positional substitutions filled in at runtime.
- The **numbers must not change** — they are the program's string ids.
- `[` `]` `{` `}` may not appear in text (used internally).

A data line is:

```
<whitespace> <id:decimal> <whitespace> "<text>"
```

Example (verbatim from `default2.lng`):

```
   1    "\n"
   7    "File error opening '%1'"
  27    "%1 is new chairman of %2!"
```

## Escapes

| sequence | meaning |
|----------|---------|
| `\n` | line break |
| `\r` | carriage return |
| `\t` | tab |
| `\"` | double quote |
| `\\` | backslash |
| other `\x` | preserved literally as `\x` |

## Parser behaviour

- Unknown / malformed lines are skipped, never fatal.
- Trailing content after the closing quote is ignored (real files contain
  trailing spaces).
- Duplicate ids: first occurrence wins for `find()`.

## CLI

```
lngdump <file.lng> [--all | --id N]
```
