param(
    [Parameter(Mandatory=$true)][string]$DeviceAddress,
    [ValidateRange(30,300)][int]$Seconds = 180
)
$ErrorActionPreference = 'Stop'
$captureDir = Join-Path $env:TEMP ('librespot-media-etw-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $captureDir | Out-Null
$path = Join-Path $captureDir 'events.jsonl'
$socket = New-Object System.Net.WebSockets.ClientWebSocket
$socket.Options.Proxy = $null
$socket.Options.SetRequestHeader('Origin', "http://$DeviceAddress")
$deadline = New-Object System.Threading.CancellationTokenSource
$deadline.CancelAfter([TimeSpan]::FromSeconds($Seconds))
$writer = New-Object IO.StreamWriter($path, $false, [Text.Encoding]::UTF8)
$writer.AutoFlush = $true
try {
    $socket.ConnectAsync([Uri]"ws://$DeviceAddress/api/etw/session/realtime", $deadline.Token).GetAwaiter().GetResult()
    $providers = @(
        '5B1C79F0-A7B1-4FB1-A3C7-374528A7B075', # Windows-Media-BackgroundPolicy
        '770ca594-b467-4811-b355-28f5e5706987', # ApplicationResourceManagementSystem
        '71E0AC1E-CFA2-447C-91C7-4F307030F2FC', # Phone AudioSrvPolicyManager
        '6a1f212d-9fc8-4878-bcf4-4be623b84525'  # Windows-Media-Playback
    )
    foreach ($provider in $providers) {
        $bytes = [Text.Encoding]::UTF8.GetBytes("provider $provider enable 5")
        $segment = New-Object 'ArraySegment[byte]' -ArgumentList (,$bytes)
        $socket.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $deadline.Token).GetAwaiter().GetResult()
    }
    Write-Output "CAPTURE READY: $path; duration=$Seconds seconds"
    $buffer = New-Object byte[] 65536
    $segment = New-Object 'ArraySegment[byte]' -ArgumentList (,$buffer)
    $message = New-Object IO.MemoryStream
    while (-not $deadline.IsCancellationRequested -and $writer.BaseStream.Position -lt 33554432) {
        $received = $socket.ReceiveAsync($segment, $deadline.Token).GetAwaiter().GetResult()
        if ($received.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) { break }
        $message.Write($buffer, 0, $received.Count)
        if ($message.Length -gt 4194304) { throw 'ETW message exceeded capture bound' }
        if ($received.EndOfMessage) {
            $writer.WriteLine([Text.Encoding]::UTF8.GetString($message.ToArray()))
            $message.SetLength(0)
        }
    }
} catch {
    if (-not $deadline.IsCancellationRequested) { throw }
} finally {
    $socket.Abort()
    $socket.Dispose()
    $writer.Dispose()
    $deadline.Dispose()
    Write-Output "CAPTURE CLOSED: $path"
}
