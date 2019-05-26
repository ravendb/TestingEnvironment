$remoteurls=@("te1", "te2", "te3")
$renewInstallation = $true
$renewDatadir = $true

$ravenport=9105
$downloadlink="https://daily-builds.s3.amazonaws.com/RavenDB-4.2.0-windows-x64.zip"

$client='$client'
$rootpath="C:\TE"
$agentPort=9123


foreach ($remoteurl in $remoteurls)
{
    Write-Host ""
    Write-Host "============"
    Write-Host "${remoteUrl}"
    Write-Host "============"
    Write-Host ""
    <# Kill old RavenDB instance first #>
    wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="Stop-Process -ErrorAction Ignore -Name ""Raven.Server"""; type out.html | %{$_ -replace "<br>", "`r`n"}

    if ($renewInstallation -eq $true) 
    {
        <# Download and Unzip #>
        wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="mkdir -ErrorAction Ignore $rootpath; cd $rootpath; del -ErrorAction Ignore ravendb.zip; $client = New-Object System.Net.WebClient; $client.DownloadFile(""$downloadlink"", ""$rootpath\ravendb.zip"")"; type out.html | %{$_ -replace "<br>", "`r`n"}
        wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="cd $rootpath; del -ErrorAction Ignore -Recurse $rootpath\RavenDB; Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::ExtractToDirectory(""$rootpath\ravendb.zip"", ""$rootpath\RavenDB"")"; type out.html | %{$_ -replace "<br>", "`r`n"}
    }
    
    if ($renewDatadir -eq $true) 
    {
        <# Delete DataDir #>
        wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="cd $rootpath; del -ErrorAction Ignore -Recurse $rootpath\DataDirTE"; type out.html | %{$_ -replace "<br>", "`r`n"}
    }

    <# Start Raven #>
    wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="Import-Module netsecurity; Set-ExecutionPolicy Unrestricted"; type out.html | %{$_ -replace "<br>", "`r`n"}
    wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="Remove-NetFirewallRule -DisplayName TestingEnvironment -ErrorAction Ignore; New-NetFirewallRule -DisplayName TestingEnvironment -Program ""${rootpath}\RavenDB\Server\Raven.Server.exe"""; type out.html | %{$_ -replace "<br>", "`r`n"}
    wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="Start-Process $rootpath\RavenDB\Server\Raven.Server -ArgumentList ""--PublicServerUrl=http://${remoteurl}:${ravenport} --ServerUrl=http://0.0.0.0:${ravenport} --Security.UnsecuredAccessAllowed=PublicNetwork --Setup.Mode=None --DataDir=$rootpath\DataDirTE"" -RedirectStandardOutput ""$rootpath\console.out"" -RedirectStandardError ""$rootpath\console.err"""; type out.html | %{$_ -replace "<br>", "`r`n"}    
}


foreach ($remoteurl in $remoteurls)
{
    wget -OutFile out.html -UseBasicParsing http://${remoteurl}:${agentPort}/teagent/execute/command?args="Start-Sleep 5; type $rootpath\console.out; type $rootpath\console.err"; type out.html | %{$_ -replace "<br>", "`r`n"}
}

Write-Host "Press Enter to exit..."
Read-Host