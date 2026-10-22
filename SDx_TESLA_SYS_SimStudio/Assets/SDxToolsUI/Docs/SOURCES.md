# Primary references consulted

- Unity 2022.3 IMGUI implementation and TextEditor state behavior:
  https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Modules/IMGUI/GUI.cs
  https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Modules/IMGUI/TextEditor.cs
- Unity Linux IME limitation report UUM-89108:
  https://issuetracker.unity.com/issues/10916/characters-are-converted-to-latin-letters-when-using-ime-on-linux
- Unity's Linux IME preview announcement (6000.5.0a7+, not a fix for 2022.3):
  https://discussions.unity.com/t/ime-input-support-now-available-for-linux-preview/1711291
- Unity input composition controls:
  https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Input-imeCompositionMode.html
  https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Input-compositionString.html
- Unicode Hangul structure:
  https://www.unicode.org/faq/korean.html

The composer is newly written for this update. No Unity reference-source
implementation or third-party IME library is redistributed. No font files
are included.
