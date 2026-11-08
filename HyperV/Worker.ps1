# VM Manager - request loop, appended after GpuLib.ps1 and run in a hidden
# powershell.exe. Protocol (one line each, UTF-8):
#   in:  {"script": "...", "args": [...]}
#   out: LOG <text>          progress messages while a request runs
#        RESULT <json array>  the request's output
#        ERROR <message>      the request failed
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$ProgressPreference = 'SilentlyContinue'

function Log([string]$m) {
    [Console]::Out.WriteLine('LOG ' + ($m -replace '\r?\n', ' '))
    [Console]::Out.Flush()
}

while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    try {
        $req = $line | ConvertFrom-Json
        $block = [scriptblock]::Create($req.script)
        $a = @($req.args)
        $out = @(& $block @a)
        [Console]::Out.WriteLine('RESULT ' + (ConvertTo-Json -InputObject $out -Compress -Depth 4))
    }
    catch {
        [Console]::Out.WriteLine('ERROR ' + ($_.Exception.Message -replace '\r?\n', ' '))
    }
    [Console]::Out.Flush()
}
