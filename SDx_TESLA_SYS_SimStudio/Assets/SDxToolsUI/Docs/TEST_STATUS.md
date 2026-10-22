# Verification status - v1.2.0

## Actually checked in the authoring environment

- All eight C# sources were read through a C# lexer and delimiter checks.
  This is NOT C# compilation or semantic type checking.
- An independent Python reference composer, using key and character tables
  extracted from the C# source, passed 11,181 cases: all 11,172 modern syllables
  and nine text/transition examples. This is NOT execution of the C# class.
- Static layout arithmetic checked the Run button and three matching 32-unit
  single-line fields at panel widths 230, 254, 320 and 420.
- Checked that a Connect button request never reports connection success.
- Checked that this patch has no MuJoCo/physics settings writes or shell execution.
- Package manifest hashes and ZIP CRCs verified at packaging time.

## Supplied, but NOT executed here

Tools > SDx > Run Text Input Self Checks executes the ACTUAL C# composer in Unity:
all modern syllables, selected phrases, backspace, caret/selection replacement,
length-limit cases, newlines, Unicode preservation and the connection gate.
Tools > SDx > Run UI Self Checks retains the original window/path self checks.
These checks do not automate the on-screen IMGUI controls.

## Not available in this environment

No Unity Editor/player, C# compiler, native Linux desktop IME, or Unity renderer.
Therefore actual Unity compilation and focus, fonts, caret scrolling, shortcut,
clipboard, EN/KR and Enter behavior must be tested on the user's PC.
No ROS, Teleop or scene generation backend was tested or added.

## Important Linux distinction

This update adds a local Dubeolsik composer; it does not fix or replace the
operating system IME. Auto mode uses it on Linux without upgrading Unity.
Use an English OS input source, then toggle the UI's EN/KR button or F2.
It supports modern two-set Hangul, not Hanja conversion, dictionaries or Sebeolsik.
Fonts remain a separate requirement. No font files are included.


## v1.2 Unicode Jamo fallback

- Added direct Unicode compatibility/conjoining Jamo handling and native text
  normalization fallback for older Linux Unity IMGUI paths.
- Added checks for direct Jamo text, existing composed Korean + loose Jamo,
  Latin text preservation and caret-adjacent final consonant splitting.
- Validation in this authoring environment: Python static checks only; actual
  C# tests run via Tools > SDx > Run Text Input Self Checks in Unity Editor.
- The user reported Jamo separated in both Instructions and Command fields;
  a plausible trigger is OS Korean IME delivering Unicode Jamo while the former
  built-in path recognized only ASCII keys. This is an evidence-based inference,
  not a captured Unity KeyDown trace.
