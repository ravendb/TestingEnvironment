param($remoteip, $remoteport, $hostuser, $hostip, $hostport, $scenario, $ochestratorurl)
Write-Host ""
Write-Host "###############"
Write-Host "# TE Scenario #"
Write-Host "###############"

$downloadfile="${scenario}.zip"
$prefix="C:\Users\$hostuser\TestingEnvironment"
$scenariodir="TE_Scenario"
date
hostname
Write-Host "[TE] RemoteIP     : ${remoteip}"
Write-Host "[TE] RemotePort   : ${remoteport}"
Write-Host "[TE] HostUser     : ${hostuser}"
Write-Host "[TE] DownloadFile : ${downloadfile}"
Write-Host "[TE] Prefix       : ${prefix}"
Write-Host "[TE] ScenarioDir  : ${scenariodir}"

Write-Host "[TE] Creating Unzip command function..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Unzip
{
    param([string]$zipfile, [string]$outpath)

    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipfile, $outpath)
}


Write-Host "[TE] Creating directory ${prefix}..."
New-Item -ItemType Directory -Force -Path ${prefix}
Write-Host "[TE] Deleting directory ${prefix}\${scenariodir}..."
del -Recurse "${prefix}\${scenariodir}" -ErrorAction Ignore
Write-Host "[TE] Deleting ${scenariodir}.zip..."
del "${prefix}\${scenariodir}.zip" -ErrorAction Ignore
Write-Host "[TE] Downloading http://${remoteip}:${remoteport}/${downloadfile}..."
wget "http://${remoteip}:${remoteport}/${downloadfile}" -UseBasicParsing -OutFile "${prefix}\${scenario}.zip"
Write-Host "[TE] Unzipping ${prefix}\${scenario}.zip..."
Unzip "${prefix}\${scenario}.zip" "${prefix}\${scenariodir}\"
Write-Host "[TE] Changing directory to ${prefix}\${scenariodir}\"
cd "${prefix}\${scenariodir}\${scenario}\"
Write-Host "[TE] Setting firewall ALLOW rule for ${scenario}.exe..."
Remove-NetFirewallRule -DisplayName TestingEnvironment_${scenario} -ErrorAction Ignore
New-NetFirewallRule -DisplayName TestingEnvironment_${scenario} -Program "${prefix}\${scenariodir}\${scenario}\${scenario}.exe"
Write-Host "[TE] Executing ${scenario}..."
Write-Host "${scenario}.exe ${ochestratorurl}"
$cmd="& .\${scenario}.exe ${ochestratorurl}"
Invoke-Expression $cmd

Write-Host "[TE] Exiting Script !!"


