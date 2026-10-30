function Build-LibrarySearchModern {
 param([string]$DotNet,[string]$ManagedRuntime,[string]$AssemblyName,[string[]]$Sources,[string[]]$References,[string]$Destination,[string]$OutputType='Library',[switch]$CopyReferences)
 if(!$DotNet){$DotNet='dotnet'}
 if(!(Get-Command $DotNet -ErrorAction SilentlyContinue)){throw '需要 .NET 8 SDK。可通过 LIBRARYSEARCH_DOTNET 指定便携 dotnet.exe；不会为客户安装 SDK。'}
 if($ManagedRuntime -notin @('net6windows','net8windows')){throw 'Unsupported build runtime'}
 $number=$ManagedRuntime.Substring(3,1);$framework='net'+$number+'.0-windows'
 $build=Join-Path $PSScriptRoot ('.modern-build/'+$AssemblyName)
 New-Item -ItemType Directory -Force -Path $build|Out-Null
 $escape={param($s)[Security.SecurityElement]::Escape($s)}
 $privateReferences=$CopyReferences.IsPresent.ToString().ToLowerInvariant()
 $items=(@($Sources|ForEach-Object {'<Compile Include="'+(& $escape $_)+'" />'})+@($References|ForEach-Object {'<Reference Include="'+(& $escape ([IO.Path]::GetFileNameWithoutExtension($_)))+'"><HintPath>'+(& $escape $_)+'</HintPath><Private>'+$privateReferences+'</Private></Reference>'})) -join "`n"
 $xml='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'+$framework+'</TargetFramework><OutputType>'+$OutputType+'</OutputType><UseWindowsForms>true</UseWindowsForms><UseWPF>true</UseWPF><EnableDefaultItems>false</EnableDefaultItems><GenerateAssemblyInfo>false</GenerateAssemblyInfo><AssemblyName>'+(& $escape $AssemblyName)+'</AssemblyName><PlatformTarget>x64</PlatformTarget><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings><CheckEolTargetFramework>false</CheckEolTargetFramework><NoWarn>CA1416;SYSLIB0014;WFAC010</NoWarn></PropertyGroup><ItemGroup>'+$items+'</ItemGroup></Project>'
 $xml=$xml.Replace('</PropertyGroup>','<UseAppHost>false</UseAppHost><NuGetAudit>false</NuGetAudit></PropertyGroup>')
 $project=Join-Path $build 'Build.csproj';[IO.File]::WriteAllText($project,$xml,[Text.UTF8Encoding]::new($false))
 $nuget=Join-Path $build 'NuGet.Config'
 $feed=if($env:LIBRARYSEARCH_NUGET_FEED){[IO.Path]::GetFullPath($env:LIBRARYSEARCH_NUGET_FEED)}else{'https://api.nuget.org/v3/index.json'}
 [IO.File]::WriteAllText($nuget,('<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="references" value="'+(& $escape $feed)+'" /></packageSources></configuration>'),[Text.UTF8Encoding]::new($false))
 $packages=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $PSScriptRoot '.modern-build/packages'}
 $oldTelemetry=$env:DOTNET_CLI_TELEMETRY_OPTOUT;$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
 $oldAppData=$env:APPDATA;$oldLocalData=$env:LOCALAPPDATA
 $isolatedProfile=Join-Path $PSScriptRoot '.modern-build/build-profile';New-Item -ItemType Directory -Force -Path $isolatedProfile|Out-Null
 $env:APPDATA=$isolatedProfile;$env:LOCALAPPDATA=$isolatedProfile
 $oldCliHome=$env:DOTNET_CLI_HOME;$env:DOTNET_CLI_HOME=$isolatedProfile
 $oldCert=$env:DOTNET_GENERATE_ASPNET_CERTIFICATE;$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
 try{
  & $DotNet restore $project --configfile $nuget --packages $packages --nologo -v minimal
  if($LASTEXITCODE -ne 0){throw ('Modern '+$framework+' reference restore failed')}
  & $DotNet build $project -c Release --no-restore --nologo -v minimal
  if($LASTEXITCODE -ne 0){throw ('Modern '+$framework+' compilation failed')}
 }finally{$env:DOTNET_CLI_TELEMETRY_OPTOUT=$oldTelemetry;$env:APPDATA=$oldAppData;$env:LOCALAPPDATA=$oldLocalData;$env:DOTNET_CLI_HOME=$oldCliHome;$env:DOTNET_GENERATE_ASPNET_CERTIFICATE=$oldCert}
 Copy-Item -LiteralPath (Join-Path $build ('bin/Release/'+$framework+'/'+$AssemblyName+'.dll')) -Destination $Destination -Force
}
