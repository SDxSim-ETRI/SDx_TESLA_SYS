using System;
using System.Text;

namespace SDx.Scenario1 {
  // Pure managed helper: no MuJoCo/Unity state and no natural-language guessing.
  public static class SDxSingleScenarioCommand {
    public static string Compact(string value) {
      if (string.IsNullOrWhiteSpace(value)) return "";
      var b = new StringBuilder();
      foreach (char c in value.Normalize(NormalizationForm.FormC))
        if (!char.IsWhiteSpace(c)) b.Append(c);
      return b.ToString();
    }
    public static bool IsScenario1(string value) { return Compact(value) == "\uC0C1\uD6691"; }
    public static bool IsRetiredScenario2(string value) { return Compact(value) == "\uC0C1\uD6692"; }
  }
}
