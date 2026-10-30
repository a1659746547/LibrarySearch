function Write-LibrarySearchArchive([string]$Source,[string]$Destination,[string]$Prefix=''){
 $sourcePath=[IO.Path]::GetFullPath($Source).TrimEnd('\');$boundary=$sourcePath+'\'
 if(Test-Path -LiteralPath $Destination){throw 'Archive already exists'}
 Add-Type -AssemblyName System.IO.Compression
 $stream=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 $zip=$null
 try{
  $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true,[Text.Encoding]::UTF8)
  foreach($file in (Get-ChildItem -LiteralPath $sourcePath -Recurse -File|Sort-Object FullName)){
   if(!$file.FullName.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase) -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked or invalid source file'}
   # ZIP filenames use '/' on all platforms. Compress-Archive on Windows may emit '\'.
   $name=$Prefix+$file.FullName.Substring($boundary.Length).Replace('\','/')
   $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
   $output=$entry.Open();$input=[IO.File]::OpenRead($file.FullName)
   try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
  }
 }finally{if($zip){$zip.Dispose()};$stream.Dispose()}
}
