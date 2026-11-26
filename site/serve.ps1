# Tiny static file server for previewing docs/ locally: powershell -File site\serve.ps1 [port]
param([int]$Port = 8765)
$root = Join-Path (Split-Path $PSScriptRoot) 'docs'
$types = @{ '.html' = 'text/html; charset=utf-8'; '.css' = 'text/css'; '.png' = 'image/png'; '.xml' = 'application/xml'; '.txt' = 'text/plain'; '.js' = 'text/javascript'; '.svg' = 'image/svg+xml' }
$l = New-Object Net.HttpListener
$l.Prefixes.Add("http://localhost:$Port/")
$l.Start()
"Serving $root on http://localhost:$Port/"
while ($l.IsListening) {
    $c = $l.GetContext()
    try {
        $p = [Uri]::UnescapeDataString($c.Request.Url.AbsolutePath).TrimStart('/')
        if ($p -eq '' -or $p.EndsWith('/')) { $p += 'index.html' }
        $f = [IO.Path]::GetFullPath((Join-Path $root $p))
        if ($f.StartsWith($root) -and (Test-Path $f -PathType Leaf)) {
            $b = [IO.File]::ReadAllBytes($f)
            $c.Response.ContentType = $(if ($types[[IO.Path]::GetExtension($f)]) { $types[[IO.Path]::GetExtension($f)] } else { 'application/octet-stream' })
            $c.Response.OutputStream.Write($b, 0, $b.Length)
        } else { $c.Response.StatusCode = 404 }
    } catch { $c.Response.StatusCode = 500 }
    $c.Response.Close()
}
