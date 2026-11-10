<#
.SYNOPSIS
  Package.appxmanifest のバージョンと、Microsoft Store のパッケージ ID（Identity）を書き換える。

.DESCRIPTION
  GitHub Actions のリリース（.github/workflows/release.yml）が、タグ v1.2.3 から 1.2.3.0 を作って呼ぶ。
  Identity の 3 つの値は Partner Center の「製品 ID」→「アプリ ID」（Package/Identity/Name、
  Package/Identity/Publisher、Package/Properties/PublisherDisplayName）の値。渡したものだけを書き換える。
  手元で一度実行してコミットしてもよい（docs/STORE_RELEASE.md）。

.EXAMPLE
  ./scripts/Set-PackageIdentity.ps1 -Version 1.2.3.0
  ./scripts/Set-PackageIdentity.ps1 -IdentityName 12345Ryokugyoku.RevTorqueInsight -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" -PublisherDisplayName Ryokugyoku
#>
param(
    [string]$Version,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\Package.appxmanifest')
)

$ErrorActionPreference = 'Stop'

if ($Version -and $Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.0$') {
    throw "Version は 4 つの数字で、最後は 0 にする（Store の決まり）：'$Version'"
}
if ($Publisher -and $Publisher -notmatch '^CN=') {
    throw "Publisher は Partner Center の値（CN=... の形）をそのまま渡す：'$Publisher'"
}

$path = (Resolve-Path $ManifestPath).Path
$bytes = [System.IO.File]::ReadAllBytes($path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
if ($hasBom) { $text = $text.Substring(1) }

function Set-Attribute([string]$source, [string]$element, [string]$attribute, [string]$value) {
    $pattern = "(<$element\b[^>]*?\s$attribute=`")[^`"]*(`")"
    if ($source -notmatch $pattern) { throw "$element の $attribute が見つからない" }
    $escaped = [System.Security.SecurityElement]::Escape($value)
    return [regex]::Replace($source, $pattern, { param($m) $m.Groups[1].Value + $escaped + $m.Groups[2].Value }, 1)
}

if ($Version) { $text = Set-Attribute $text 'Identity' 'Version' $Version }
if ($IdentityName) { $text = Set-Attribute $text 'Identity' 'Name' $IdentityName }
if ($Publisher) { $text = Set-Attribute $text 'Identity' 'Publisher' $Publisher }
if ($PublisherDisplayName) {
    $pattern = '(<PublisherDisplayName>)[^<]*(</PublisherDisplayName>)'
    if ($text -notmatch $pattern) { throw 'PublisherDisplayName が見つからない' }
    $escaped = [System.Security.SecurityElement]::Escape($PublisherDisplayName)
    $text = [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $escaped + $m.Groups[2].Value }, 1)
}

[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($hasBom))

$xml = [xml]$text
$identity = $xml.Package.Identity
Write-Host "Identity: Name=$($identity.Name) Publisher=$($identity.Publisher) Version=$($identity.Version)"
Write-Host "PublisherDisplayName: $($xml.Package.Properties.PublisherDisplayName)"
