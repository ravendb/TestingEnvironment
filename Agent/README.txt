Launch RavenDB on remote Windows host
=====================================

* On remote host, from PowerShell window opened as Administrator:
Service: Start service `Windows Remote Management`

  (Can be found in services windows: `Run->services.msc`)
Open PowerShell as *Administrator* and write:


   Enable-PSRemoting -SkipNetworkProfileCheck


   WinRM quickconfig

   (answer 'Y')


   ipconfig


   [System.Security.Principal.WindowsIdentity]::GetCurrent().Name


   hostname
Enabling RDC might be usefull too.

* On local host, from PowerShell window opened as Administrator:
run createSecurePasswordString.ps1 to create secure password file
.\LaunchRemoteRavenCommand.ps1 <remotehost ip> <poweruser> <domain>

Launch client scenario on remote Windows host
=============================================

Open PowerShell as *Administrator* and write:

.\LaunchRemoteScenarioCommand.ps1 <remotehost ip> <poweruser> <domain> <scneario> <orchestrator url>
