using System.Net;
using System.Text;

namespace PulseCheck;

/// <summary>Builds a self-contained, localised HTML report.</summary>
public static class Report
{
    const string Css = @"
body{font-family:Segoe UI,Arial,sans-serif;background:#f4f6f8;color:#222;margin:0;padding:24px}
.wrap{max-width:980px;margin:auto}h1{margin:0 0 4px}h2{margin:28px 0 8px;font-size:18px}
.sub{color:#666;margin-bottom:20px}.sum{display:flex;gap:14px;margin-bottom:10px;flex-wrap:wrap}
.card{background:#fff;border-radius:10px;padding:14px 20px;box-shadow:0 1px 3px #0001;text-align:center;min-width:110px}
.card b{display:block;font-size:30px}
table{width:100%;border-collapse:collapse;background:#fff;border-radius:10px;overflow:hidden;box-shadow:0 1px 3px #0001}
td{padding:10px 12px;border-bottom:1px solid #eee;vertical-align:top;font-size:14px}
td.b{width:84px}td.n{width:230px;font-weight:600}
.badge{display:inline-block;padding:2px 9px;border-radius:10px;color:#fff;font-size:12px;font-weight:700}
.Ok{background:#2e9e5b}.Warn{background:#d99a1c}.Fail{background:#d0453a}.Info{background:#4a7fb5}
.fix{margin-top:5px;color:#a15c00;font-size:13px}.foot{color:#888;font-size:12px;margin-top:24px}";

    public static string Build(IReadOnlyList<CheckResult> res)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        int fail = res.Count(r => r.Status == Status.Fail), warn = res.Count(r => r.Status == Status.Warn),
            ok = res.Count(r => r.Status == Status.Ok), score = Scanner.Score(res);
        string sc = score >= 85 ? "#2e9e5b" : score >= 60 ? "#d99a1c" : "#d0453a";

        var sb = new StringBuilder();
        sb.Append($"<!DOCTYPE html><html lang=\"{Loc.Current}\"><head><meta charset=\"utf-8\"><title>{E(Loc.T("ui.report_title"))}</title><style>{Css}</style></head><body><div class=\"wrap\">");
        sb.Append($"<h1>{E(Loc.T("ui.report_title"))}</h1><div class=\"sub\">{E(Environment.MachineName)} · {DateTime.Now:g}</div>");
        sb.Append("<div class=\"sum\">");
        sb.Append($"<div class=\"card\"><b style=\"color:{sc}\">{score}</b>{E(Loc.T("ui.score"))}</div>");
        sb.Append($"<div class=\"card\"><b style=\"color:#d0453a\">{fail}</b>{E(Loc.T("ui.problems"))}</div>");
        sb.Append($"<div class=\"card\"><b style=\"color:#d99a1c\">{warn}</b>{E(Loc.T("ui.warnings"))}</div>");
        sb.Append($"<div class=\"card\"><b style=\"color:#2e9e5b\">{ok}</b>{E(Loc.T("ui.ok"))}</div></div>");

        foreach (var g in res.GroupBy(r => r.Category))
        {
            sb.Append($"<h2>{E(Loc.T(g.Key))}</h2><table>");
            foreach (var r in g.OrderBy(r => r.Status))
            {
                string word = Loc.T(r.Status switch { Status.Ok => "s.ok", Status.Warn => "s.warn", Status.Fail => "s.fail", _ => "s.info" });
                string fix = r.Fix == null ? "" : $"<div class=\"fix\">&rarr; {E(r.Fix.Text)}</div>";
                sb.Append($"<tr><td class=\"b\"><span class=\"badge {r.Status}\">{E(word)}</span></td><td class=\"n\">{E(r.Name.Text)}</td><td>{E(r.Detail.Text)}{fix}</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append($"<div class=\"foot\">{E(Loc.T("ui.privacy"))}</div></div></body></html>");
        return sb.ToString();
    }
}
