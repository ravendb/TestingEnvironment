param($remoteip, $remoteport, $hostuser, $downloadfile, $hostip, $hostport)
Write-Host ""
Write-Host "###############"
Write-Host "# TE RavenDB  #"
Write-Host "###############"

$prefix="C:\Users\$hostuser\TestingEnvironment"
$ravendbdir="TE_RavenDB"
date
hostname
Write-Host "[TE] RemoteIP     : ${remoteip}"
Write-Host "[TE] RemotePort   : ${remoteport}"
Write-Host "[TE] HostUser     : ${hostuser}"
Write-Host "[TE] DownloadFile : ${downloadfile}"
Write-Host "[TE] Prefix       : ${prefix}"
Write-Host "[TE] RavenDB Dir  : ${ravendbdir}"

Write-Host "[TE] Creating Unzip command function..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Unzip
{
    param([string]$zipfile, [string]$outpath)

    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipfile, $outpath)
}

Write-Host "[TE] Creating directory ${prefix}..."
New-Item -ItemType Directory -Force -Path ${prefix}
Write-Host "[TE] Deleting directory ${prefix}\${ravendbdir}..."
del -Recurse "${prefix}\${ravendbdir}" -ErrorAction Ignore
Write-Host "[TE] Deleting ${ravendbdir}.zip..."
del "${prefix}\${ravendbdir}.zip" -ErrorAction Ignore
Write-Host "[TE] Downloading http://${remoteip}:${remoteport}/${downloadfile}..."
wget "http://${remoteip}:${remoteport}/${downloadfile}" -UseBasicParsing -OutFile "${prefix}\${ravendbdir}.zip"
Write-Host "[TE] Unzipping ${prefix}\${ravendbdir}.zip..."
Unzip "${prefix}\${ravendbdir}.zip" "${prefix}\${ravendbdir}\"
Write-Host "[TE] Changing directory to ${prefix}\${ravendbdir}\Server..."
cd "${prefix}\${ravendbdir}\Server"
Write-Host "[TE] Setting firewall ALLOW rule for Raven.Server.exe..."
Remove-NetFirewallRule -DisplayName TestingEnvironment -ErrorAction Ignore
New-NetFirewallRule -DisplayName TestingEnvironment -Program "${prefix}\${ravendbdir}\Server\Raven.Server.exe"
Write-Host "[TE] Executing RavenDB..."
Write-Host ".\Raven.Server.exe --PublicServerUrl=http://${hostip}:${hostport} --ServerUrl=http://${hostip}:${hostport} -Security.UnsecuredAccessAllowed=PublicNetwork --Setup.Mode=None"

.\Raven.Server.exe --PublicServerUrl=http://${hostip}:${hostport} --ServerUrl=http://${hostip}:${hostport} --Security.UnsecuredAccessAllowed=PublicNetwork --Setup.Mode=None

Write-Host "[TE] Exiting Script !!"


