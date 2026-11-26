<#
  Generates the static site into ../docs from templates and per-language strings.
  Usage: powershell -File site\build.ps1
#>
$ErrorActionPreference = 'Stop'
$site = $PSScriptRoot
$root = Split-Path $site
$out  = Join-Path $root 'docs'
$base = 'https://brillant000.github.io/PulseCheck/'
$ghrepo = 'Brillant000/PulseCheck'
$repo = "https://github.com/$ghrepo"
$langs = 'en', 'fr', 'es', 'de'
$locales = @{ en = 'en_US'; fr = 'fr_FR'; es = 'es_ES'; de = 'de_DE' }
$pages = 'index', 'privacy'

$version = ([xml](Get-Content "$root\src\PulseCheck.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$size = 63   # approximate download size in MB, shown as "About 63 MB"

$script:missing = New-Object System.Collections.Generic.HashSet[string]
function Expand([string]$text, [hashtable]$vars) {
    $ev = [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $k = $m.Groups[1].Value
        if ($vars.ContainsKey($k)) { return [string]$vars[$k] }
        [void]$script:missing.Add($k); return $m.Value
    }
    for ($i = 0; $i -lt 3; $i++) { $text = [regex]::Replace($text, '\{\{([\w.\-]+)\}\}', $ev) }
    $text
}
function Prefix($l) { if ($l -eq 'en') { '' } else { "$l/" } }
function RootOf($l) { if ($l -eq 'en') { '' } else { '../' } }
function Url($l, $p) { $base + (Prefix $l) + $(if ($p -eq 'index') { '' } else { "$p.html" }) }
function Rel($from, $to, $p) { (RootOf $from) + (Prefix $to) + "$p.html" }

$partials = @{}
foreach ($n in 'head', 'header', 'footer') { $partials[$n] = [IO.File]::ReadAllText("$site\templates\_$n.html") }

foreach ($l in $langs) {
    $strings = Get-Content "$site\lang\$l.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($p in $pages) {
        $v = @{}
        $strings.PSObject.Properties | ForEach-Object { $v[$_.Name] = $_.Value }
        $v.lang = $l; $v.locale = $locales[$l]; $v.root = RootOf $l; $v.home = './'
        $v.base = $base; $v.repo = $repo; $v.ghrepo = $ghrepo; $v.version = $version; $v.size = $size
        $v.download = "$repo/releases/latest/download/PulseCheck.exe"
        $v.canonical = Url $l $p
        $v.skip = $strings.skip
        $v.title = if ($p -eq 'index') { $strings.'meta.title' } else { $strings.'pv.title' + ' – PulseCheck' }
        $v.description = $strings.'meta.desc'
        $v.alts = (($langs | ForEach-Object { "<link rel=`"alternate`" hreflang=`"$_`" href=`"$(Url $_ $p)`">" }) -join "`n") + "`n<link rel=`"alternate`" hreflang=`"x-default`" href=`"$(Url 'en' $p)`">"
        $v.switcher = '<nav class="langs" aria-label="Language">' + (($langs | ForEach-Object {
            $cur = if ($_ -eq $l) { ' aria-current="true"' } else { '' }
            "<a href=`"$(Rel $l $_ $p)`" lang=`"$_`" hreflang=`"$_`"$cur>$($_.ToUpper())</a>"
        }) -join '') + '</nav>'
        $html = [IO.File]::ReadAllText("$site\templates\$p.html")
        foreach ($n in $partials.Keys) { $html = $html.Replace("{{inc:$n}}", $partials[$n]) }
        $html = Expand $html $v
        $dir = if ($l -eq 'en') { $out } else { Join-Path $out $l }
        New-Item -ItemType Directory -Force $dir | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir "$p.html"), $html, (New-Object Text.UTF8Encoding($false)))
    }
}

Copy-Item "$site\style.css" "$out\style.css" -Force
$urls = foreach ($l in $langs) { foreach ($p in $pages) { Url $l $p } }
$sm = '<?xml version="1.0" encoding="UTF-8"?>' + "`n" + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' + "`n" +
      (($urls | ForEach-Object { "  <url><loc>$_</loc></url>" }) -join "`n") + "`n</urlset>`n"
[IO.File]::WriteAllText("$out\sitemap.xml", $sm, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText("$out\robots.txt", "User-agent: *`nAllow: /`n`nSitemap: ${base}sitemap.xml`n", (New-Object Text.UTF8Encoding($false)))

# 404 page (served by GitHub Pages for unknown URLs)
$nf = @"
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Page not found – PulseCheck</title><meta name="robots" content="noindex"><link rel="stylesheet" href="/PulseCheck/style.css"></head>
<body><main class="wrap prose"><h1>Page not found</h1><p>The page you are looking for does not exist.</p><p><a class="btn" href="/PulseCheck/">Go to PulseCheck</a></p></main></body></html>
"@
[IO.File]::WriteAllText("$out\404.html", $nf, (New-Object Text.UTF8Encoding($false)))

if ($script:missing.Count -gt 0) { Write-Warning ("Missing keys: " + ($script:missing -join ', ')) } else { "OK: no missing keys" }
"Built $($langs.Count * $pages.Count) pages, version $version"
