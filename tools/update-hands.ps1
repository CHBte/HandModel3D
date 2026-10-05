<#
손 모양 갱신 파이프라인 (sample-json → 손 모델 3D → SVG → OpenTypingPlus 손가락 레이어)

    ① 추출  : 손 모델 3D 를 -svgexportall 로 한 번 실행해 sample-json 전체를 SVG 로 뽑는다
              (파일마다 열 손가락 끝 좌표를 777x260 단위로 로그에 남긴다).
    ② 선택  : 물리 키마다 쓸 '확정본'을 정한다. 목록 파일이 있으면 그 지정이 우선이고,
              없는 키는 기본 라벨 파일을 자동으로 고른다.
              ※ physical-key-master.md 의 계약: 확정본은 원래 사용자가 고르는 것이다.
                 자동 선택은 목록에 없는 키를 위한 편의 기본값일 뿐이다.
    ③ 검증  : 고른 포즈마다 담당 손가락 끝이 그 키 사각형 안에 있는지, 윗글쇠 조합이면
              반대 손 소지가 해당 [Shift] 위에 있는지 확인한다. 실패가 있으면 설치하지 않는다.
    ④ 설치  : OpenTyping\hands 에 {행}-{열}-{손}.svg / home-*.svg / lshift.svg / rshift.svg 로 복사.
    ⑤ 배포  : OpenTypingPlus 를 빌드하고 build.bat 으로 배포한다.
    ⑥ 배선  : 배포본을 -posesources 로 돌려, 키마다 손가락 레이어가 **실제로 읽은 파일 이름**이
              기대와 같은지 대조한다(설치 누락·엉뚱한 파일 로드 잡기).

사용 예
    .\update-hands.ps1                     # 전체(추출→검증→설치→배포)
    .\update-hands.ps1 -WriteList          # 확정본 목록 파일만 현재 자동 선택으로 만들어 본다
    .\update-hands.ps1 -SkipExport         # 이미 뽑아 둔 SVG 로 설치만 다시
    .\update-hands.ps1 -NoDeploy           # 설치까지만(빌드·배포 생략)

확정본 목록 파일(기본: tools\확정본.txt) 형식 — '#' 로 시작하는 줄은 주석
    2-3        [ㄹ]_2026-08-11-183411
    home       기본자세_확정
    lshift     왼쪽[Shift]_2026-08-11-184831
    rshift     오른쪽[Shift]_2026-08-11-190931
#>
[CmdletBinding()]
param(
    [string]$Poses   = "",      # 상태 json 폴더 (기본: HandModel3D\sample-json)
    [string]$SvgOut  = "",      # SVG 중간 산출물 폴더 (기본: HandModel3D\tools\svg-out)
    [string]$Hands   = "",      # 설치 대상 (기본: OpenTypingPlus\OpenTyping\hands)
    [string]$List    = "",      # 확정본 목록 (기본: HandModel3D\tools\확정본.txt)
    [string]$Exe     = "",      # 손 모델 3D 실행 파일 (기본: HandModel3D\build\HandModel3D.exe)
    [switch]$SkipExport,        # 추출 건너뛰기(기존 SvgOut 사용)
    [switch]$NoDeploy,          # OTP 빌드·배포 생략
    [switch]$WriteList,         # 확정본 목록만 만들고 종료
    [switch]$Force              # 검증 실패해도 설치 강행
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)     # ...\claud6_typing_Csharp
if ($Poses  -eq "") { $Poses  = Join-Path $root "HandModel3D\sample-json" }
if ($SvgOut -eq "") { $SvgOut = Join-Path $PSScriptRoot "svg-out" }
if ($Hands  -eq "") { $Hands  = Join-Path $root "OpenTypingPlus\OpenTyping\hands" }
if ($List   -eq "") { $List   = Join-Path $PSScriptRoot "확정본.txt" }
if ($Exe    -eq "") { $Exe    = Join-Path $root "HandModel3D\build\HandModel3D.exe" }
$master = Join-Path $root "HandModel3D\physical-key-master.json"

function Read-Utf8($path) { return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) }
function Write-Utf8($path, $text) { [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false)) }
function Say($msg) { Write-Host $msg }

# ---------- 마스터 표: 라벨 → 물리 키, 키 → 손·손가락 ----------
$mj = Read-Utf8 $master | ConvertFrom-Json
$full = @{ '/'='／'; '\'='＼'; '?'='？'; '*'='＊'; ':'='：'; '<'='＜'; '>'='＞'; '|'='｜'; '"'='＂' }
function Normalize-Label([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        if ($full.ContainsKey([string]$ch)) { [void]$sb.Append($full[[string]$ch]) } else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}
# ⚠️ @{} 는 대소문자를 구분하지 않아 'Q' 와 'q' 가 같은 키로 합쳐지고(윗글쇠 표시가 사라진다), 영문 자판 라벨의
#    윗글쇠 포즈가 기본 포즈로 잘못 골라지거나 Shift 검증이 건너뛰어진다. 라벨은 대소문자 구분 해시표로 둔다.
$labelToKey = New-Object System.Collections.Hashtable -ArgumentList ([System.StringComparer]::Ordinal)   # 정규화 라벨 -> "행-열|shift여부"
$keyInfo    = @{}      # "행-열" -> @{ hand; finger }
foreach ($k in $mj.keys) {
    $id = "" + $k.keyPos[0] + "-" + $k.keyPos[1]
    $keyInfo[$id] = @{ hand = $k.hand; finger = $k.finger }
    foreach ($lay in @($k.qwerty, $k.dubeolsik)) {
        foreach ($pair in @(@($lay.base, $false), @($lay.shift, $true))) {
            $v = $pair[0]
            if ($v -ne $null -and $v -ne "") {
                $n = Normalize-Label $v
                if (-not $labelToKey.ContainsKey($n)) { $labelToKey[$n] = @{ key = $id; shift = $pair[1] } }
            }
        }
    }
}

# ⚠️ 배열 리터럴 안에서는 쉼표가 곱셈보다 강하게 묶인다(@(102 + $c * 52, 104) 는
#    $c * (52,104) 로 해석돼 오류). 산술은 반드시 괄호로 감싼다.
function Key-Rect([int]$r, [int]$c) {
    if ($r -eq 0) { return @(($c * 52), 0, 50, 50) }
    if ($r -eq 1) { $w = 50; if ($c -eq 12) { $w = 70 }; return @((82 + $c * 52), 52, $w, 50) }
    if ($r -eq 2) { return @((102 + $c * 52), 104, 50, 50) }
    return @((132 + $c * 52), 156, 50, 50)
}
$LSHIFT = @(0, 156, 130, 50)
$RSHIFT = @(652, 156, 125, 50)
function Inside($pt, $rect, [double]$pad) {
    return ($pt[0] -ge $rect[0] - $pad) -and ($pt[0] -le $rect[0] + $rect[2] + $pad) -and
           ($pt[1] -ge $rect[1] - $pad) -and ($pt[1] -le $rect[1] + $rect[3] + $pad)
}

# ---------- ① 추출 ----------
# -WriteList 는 상태 json 폴더만 보면 되므로 추출을 건너뛴다(앱 실행 불필요).
if ($WriteList) { $SkipExport = $true }
if (-not $SkipExport) {
    if (-not (Test-Path -LiteralPath $Exe)) { throw "손 모델 3D 실행 파일이 없습니다: $Exe" }
    Say "① 추출: $Poses → $SvgOut"
    if (Test-Path -LiteralPath $SvgOut) {
        # 이전 추출 산출물(SVG·로그)만 든 폴더일 때만 비운다 — -SvgOut 을 문서 폴더 같은 엉뚱한 곳으로 줘도
        # 그 안의 다른 파일을 통째로 지우지 않게.
        $foreign = @(Get-ChildItem -LiteralPath $SvgOut -Force | Where-Object {
            $_.PSIsContainer -or $_.Name -notmatch '^(_export\.log|_posesources\.txt|.+\.svg|.+\.svg\.err\.txt)$' })
        if ($foreign.Count -gt 0) { throw "SvgOut 폴더에 추출 산출물이 아닌 것이 있어 비우지 않습니다: $SvgOut (예: $($foreign[0].Name))" }
        Remove-Item -LiteralPath $SvgOut -Recurse -Force
    }
    # ⚠️ Start-Process 는 -ArgumentList 의 각 원소를 따옴표 없이 공백으로 이어 붙인다. 경로에 공백이 있으면
    #    (예: C:\Users\John Smith\...) 여러 인자로 쪼개져 엉뚱한 폴더에 쓰므로 직접 따옴표로 감싼다.
    $p = Start-Process $Exe -ArgumentList @('-svgexportall', ('"' + $Poses.TrimEnd('\') + '"'), ('"' + $SvgOut.TrimEnd('\') + '"')) -PassThru
    if (-not $p.WaitForExit(600000)) { throw "추출이 10분 안에 끝나지 않았습니다." }
    Say ("   SVG {0}개" -f (Get-ChildItem -LiteralPath $SvgOut -Filter *.svg).Count)
} else {
    Say "① 추출 건너뜀 (기존 $SvgOut 사용)"
}
$logPath = Join-Path $SvgOut "_export.log"
if (-not $WriteList -and -not (Test-Path -LiteralPath $logPath)) { throw "추출 로그가 없습니다: $logPath" }

# ---------- 로그에서 포즈별 손끝 좌표 읽기 ----------
$tipsOf = @{}          # stem -> @{ L1=@(x,y); ... }
$logText = ""
if (Test-Path -LiteralPath $logPath) { $logText = Read-Utf8 $logPath }
foreach ($line in $logText -split "`r?`n") {
    $m = [regex]::Match($line, '^OK\s+(?<stem>.+?)\s+kb=\S+\s+kbScale=\S+\s+(?<tips>.*)$')
    if (-not $m.Success) { continue }
    $t = @{}
    foreach ($g in [regex]::Matches($m.Groups['tips'].Value, '([LR]\d)=\(([-\d.]+),([-\d.]+)\)')) {
        $t[$g.Groups[1].Value] = @([double]$g.Groups[2].Value, [double]$g.Groups[3].Value)
    }
    $tipsOf[$m.Groups['stem'].Value] = $t
}
Say ("   로그에서 포즈 {0}개 읽음" -f $tipsOf.Count)

# ---------- ② 확정본 선택 ----------
$auto = @{}            # "행-열" -> @{ stem; shift }
$special = @{}         # home / lshift / rshift -> stem
foreach ($f in (Get-ChildItem -LiteralPath $Poses -Filter *.json | Sort-Object Name)) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    if ($stem.StartsWith("기본자세")) { if ($stem -eq "기본자세_확정" -or -not $special.ContainsKey("home")) { $special["home"] = $stem }; continue }
    # 대괄호는 -like 에서 와일드카드라 문자열 비교로 판정한다
    if ($stem.StartsWith("왼쪽[Shift]"))   { $special["lshift"] = $stem; continue }
    if ($stem.StartsWith("오른쪽[Shift]")) { $special["rshift"] = $stem; continue }
    $m = [regex]::Match($stem, '^\[(.+?)\]_')
    if (-not $m.Success) { continue }
    $lab = $m.Groups[1].Value
    if (-not $labelToKey.ContainsKey($lab)) { Say "   ! 라벨 매칭 실패: $stem"; continue }
    $info = $labelToKey[$lab]
    $id = $info.key
    if (-not $auto.ContainsKey($id) -or ($auto[$id].shift -and -not $info.shift)) {
        $auto[$id] = @{ stem = $stem; shift = $info.shift }
    }
}

# 목록 파일 읽기(있으면 우선)
$chosen = @{}
foreach ($id in $auto.Keys) { $chosen[$id] = $auto[$id].stem }
foreach ($k in @("home", "lshift", "rshift")) { if ($special.ContainsKey($k)) { $chosen[$k] = $special[$k] } }
$fromList = 0
if (Test-Path -LiteralPath $List) {
    foreach ($line in (Read-Utf8 $List) -split "`r?`n") {
        $t = $line.Trim()
        if ($t -eq "" -or $t.StartsWith("#")) { continue }
        $parts = $t -split '\s+', 2
        if ($parts.Count -lt 2) { continue }
        $chosen[$parts[0].Trim()] = $parts[1].Trim()
        $fromList++
    }
    Say "② 확정본: 목록 파일에서 $fromList 건 지정 ($List)"
} else {
    Say "② 확정본: 목록 파일 없음 — 전부 자동 선택(기본 라벨 우선)"
}

if ($WriteList) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# 확정본 목록 — '행-열' 또는 home/lshift/rshift 다음에 원본 json 이름(확장자 없이)")
    [void]$sb.AppendLine("# 이 파일에 적은 것이 자동 선택보다 우선합니다. 줄을 지우면 자동 선택으로 돌아갑니다.")
    foreach ($k in @("home", "lshift", "rshift")) { if ($chosen.ContainsKey($k)) { [void]$sb.AppendLine(("{0,-8} {1}" -f $k, $chosen[$k])) } }
    foreach ($id in ($chosen.Keys | Where-Object { $_ -match '^\d+-\d+$' } | Sort-Object { [int]($_ -split '-')[0] }, { [int]($_ -split '-')[1] })) {
        [void]$sb.AppendLine(("{0,-8} {1}" -f $id, $chosen[$id]))
    }
    Write-Utf8 $List $sb.ToString()
    Say "   → 목록 파일 작성: $List"
    return
}

# ---------- ③ 검증 ----------
Say "③ 검증: 담당 손가락이 그 키를 짚는가"
$fails = New-Object System.Collections.ArrayList
$checked = 0
foreach ($id in ($chosen.Keys | Where-Object { $_ -match '^\d+-\d+$' } | Sort-Object)) {
    $stem = $chosen[$id]
    $rc = $id -split '-'
    $r = [int]$rc[0]; $c = [int]$rc[1]
    $info = $keyInfo[$id]
    if ($info -eq $null -or $info.finger -eq $null) { continue }
    if (-not $tipsOf.ContainsKey($stem)) { [void]$fails.Add("$id : 추출 로그에 '$stem' 없음"); continue }
    $checked++
    $tips = $tipsOf[$stem]
    $pt = $tips[$info.finger]
    if ($pt -eq $null) { [void]$fails.Add("$id : 손끝 좌표 없음 ($($info.finger))"); continue }
    if (-not (Inside $pt (Key-Rect $r $c) 4)) {
        [void]$fails.Add(("{0} : {1} 이 키 밖 ({2:N1},{3:N1}) — {4}" -f $id, $info.finger, $pt[0], $pt[1], $stem))
    }
    # 윗글쇠 조합 포즈면 반대 손 소지가 해당 [Shift] 위에 있어야 한다
    $isShiftPose = $false
    $m = [regex]::Match($stem, '^\[(.+?)\]_')
    if ($m.Success -and $labelToKey.ContainsKey($m.Groups[1].Value)) { $isShiftPose = $labelToKey[$m.Groups[1].Value].shift }
    if ($isShiftPose) {
        if ($info.hand -eq "left") { $other = "R0"; $rect = $RSHIFT } else { $other = "L1"; $rect = $LSHIFT }
        $p2 = $tips[$other]
        if ($p2 -eq $null -or -not (Inside $p2 $rect 8)) { [void]$fails.Add("$id : 반대 손 $other 이 [Shift] 밖 — $stem") }
    }
}
foreach ($k in @("lshift", "rshift")) {
    if (-not $chosen.ContainsKey($k)) { [void]$fails.Add("$k 포즈 없음"); continue }
    $tips = $tipsOf[$chosen[$k]]
    if ($k -eq "lshift") { $f = "L1"; $rect = $LSHIFT } else { $f = "R0"; $rect = $RSHIFT }
    if ($tips -eq $null -or $tips[$f] -eq $null -or -not (Inside $tips[$f] $rect 8)) { [void]$fails.Add("$k : $f 이 [Shift] 밖") }
}
Say ("   검사 {0}건, 실패 {1}건" -f $checked, $fails.Count)
foreach ($x in $fails) { Say "   ! $x" }
if ($fails.Count -gt 0 -and -not $Force) { throw "검증 실패로 설치를 중단합니다(-Force 로 강행 가능)." }

# ---------- ④ 설치 ----------
Say "④ 설치: $Hands"
# 바꿔 넣을 SVG 를 먼저 임시 폴더에 전부 모은다. 원본이 하나라도 없으면(예: 기본자세 포즈가 '키보드 꺼짐'으로
# 건너뛰어져 SVG 가 안 만들어진 경우) 기존 hands 폴더를 지우기 전에 멈춘다 — 안 그러면 옛 그림만 지워진 채
# '완료'가 찍힌다.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("update-hands-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage | Out-Null
$manifest = New-Object System.Collections.ArrayList
[void]$manifest.Add("# update-hands.ps1 이 설치한 손 모양 (설치 이름 → 원본 SVG)")
$installed = 0
$missing = 0
function Install-Svg($stem, $hand, $outName) {
    # ⚠️ 원본 이름에 [ ] 가 들어 있다(예: [ㄱ]_…). Test-Path·Copy-Item 은 그것을 와일드카드로
    #    해석하므로 반드시 -LiteralPath 를 써야 한다.
    $src = Join-Path $SvgOut ("{0}-{1}.svg" -f $stem, $hand)
    if (-not (Test-Path -LiteralPath $src)) { Say "   ! 없음: $src"; $script:missing++; return $false }
    Copy-Item -LiteralPath $src -Destination (Join-Path $stage $outName) -Force
    [void]$script:manifest.Add(("{0}`t{1}-{2}.svg" -f $outName, $stem, $hand))
    $script:installed++
    return $true
}
if ($chosen.ContainsKey("home")) {
    [void](Install-Svg $chosen["home"] "left" "home-left.svg")
    [void](Install-Svg $chosen["home"] "right" "home-right.svg")
}
if ($chosen.ContainsKey("lshift")) { [void](Install-Svg $chosen["lshift"] "left" "lshift.svg") }
if ($chosen.ContainsKey("rshift")) { [void](Install-Svg $chosen["rshift"] "right" "rshift.svg") }
foreach ($id in ($chosen.Keys | Where-Object { $_ -match '^\d+-\d+$' } | Sort-Object)) {
    $info = $keyInfo[$id]
    if ($info -eq $null -or $info.hand -eq $null) { Say "   - $id 건너뜀(담당 손 미정)"; continue }
    [void](Install-Svg $chosen[$id] $info.hand ("{0}-{1}.svg" -f $id, $info.hand))
}
if ($missing -gt 0 -and -not $Force) {
    Remove-Item -LiteralPath $stage -Recurse -Force
    throw "설치할 SVG 가 $missing 개 없어 기존 손 모양을 그대로 둡니다(-Force 로 강행 가능)."
}
try {
    if (-not (Test-Path -LiteralPath $Hands)) { New-Item -ItemType Directory -Path $Hands | Out-Null }
    Get-ChildItem -LiteralPath $Hands -Filter *.svg | Remove-Item -Force
    foreach ($f in (Get-ChildItem -LiteralPath $stage -Filter *.svg)) {
        Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $Hands $f.Name) -Force
    }
} finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue   # 도중에 실패해도 임시 폴더는 남기지 않는다
}
Write-Utf8 (Join-Path $Hands "_manifest.txt") (($manifest -join "`r`n") + "`r`n")
Say "   설치 $installed 개"

# ---------- ⑤ 빌드·배포 ----------
if ($NoDeploy) {
    Say "⑤ 배포 생략(-NoDeploy)"
} else {
    Say "⑤ OpenTypingPlus 빌드·배포"
    $proj = Join-Path $root "OpenTypingPlus\OpenTyping\OpenTyping.csproj"
    & dotnet build $proj -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "OTP 빌드 실패" }
    & (Join-Path $root "OpenTypingPlus\build.bat")
    if ($LASTEXITCODE -ne 0) { throw "OTP 배포 실패" }
}

# ---------- ⑥ 배선 확인: OTP 가 실제로 읽은 파일 이름 대조 ----------
# 그림을 비교하는 대신, 손가락 레이어가 키마다 어떤 hands\ 파일을 읽었는지 이름만 확인한다
# (설치 누락·엉뚱한 파일 로드를 값싸게 잡는다. 그림 자체의 정확성은 ③에서 좌표로 이미 봤다).
# 실행 파일 이름은 <260830_2-4-3>(3)에서 OpenTypingPlus.exe → 열린타자+.exe 로 바뀌었다.
$otpExe = Join-Path $root "OpenTypingPlus\build\열린타자+.exe"
if ($NoDeploy -or -not (Test-Path -LiteralPath $otpExe)) {
    Say "⑥ 배선 확인 생략(배포본 없음)"
} else {
    Say "⑥ 배선 확인: OTP 가 실제로 읽은 파일 이름 대조"
    $srcTxt = Join-Path $SvgOut "_posesources.txt"
    # 열린타자+ 의 개발용 진단 스위치는 환경변수 OTP_DIAG=1 일 때만 동작한다(배포본이 아무 경로에나 파일을
    # 쓰는 일이 없도록).
    $env:OTP_DIAG = "1"
    try { & $otpExe -posesources $srcTxt | Out-Null }
    finally { Remove-Item Env:OTP_DIAG -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (-not (Test-Path -LiteralPath $srcTxt)) { throw "배선 확인 실패: $srcTxt 가 만들어지지 않았습니다." }

    $wrong = New-Object System.Collections.ArrayList
    $rows = 0
    foreach ($line in (Read-Utf8 $srcTxt) -split "`r?`n") {
        $m = [regex]::Match($line.Trim(), '^(?<id>home|\d+-\d+)\s+(?<mode>base|shift)\s+left=(?<l>\S+)\s+right=(?<r>\S+)$')
        if (-not $m.Success) { continue }
        $rows++
        $id = $m.Groups['id'].Value
        if ($id -eq "home") {
            if ($m.Groups['l'].Value -ne "home-left.svg" -or $m.Groups['r'].Value -ne "home-right.svg") {
                [void]$wrong.Add("home : left=$($m.Groups['l'].Value) right=$($m.Groups['r'].Value)")
            }
            continue
        }
        $isShift = $m.Groups['mode'].Value -eq "shift"
        $info = $keyInfo[$id]
        if ($info -eq $null -or $info.hand -eq $null) {
            # 담당 손 미정 키는 SetPose 가 기본자세로 돌아간다(⧵| 는 <261005_5> 로 오른손 R0 에 배정되어 지금은 없다)
            $expL = "home-left.svg"; $expR = "home-right.svg"
        } elseif ($info.hand -eq "left") {
            $expL = "$id-left.svg"
            if ($isShift) { $expR = "rshift.svg" } else { $expR = "home-right.svg" }
        } else {
            $expR = "$id-right.svg"
            if ($isShift) { $expL = "lshift.svg" } else { $expL = "home-left.svg" }
        }
        if ($m.Groups['l'].Value -ne $expL -or $m.Groups['r'].Value -ne $expR) {
            [void]$wrong.Add(("{0} {1} : 읽음 left={2} right={3} / 기대 left={4} right={5}" -f `
                $id, $m.Groups['mode'].Value, $m.Groups['l'].Value, $m.Groups['r'].Value, $expL, $expR))
        }
    }
    Say ("   {0}행 확인, 어긋남 {1}건" -f $rows, $wrong.Count)
    foreach ($x in $wrong) { Say "   ! $x" }
    if ($wrong.Count -gt 0 -and -not $Force) { throw "배선 확인 실패(-Force 로 무시 가능)." }
}

Say "완료: 손 모양 $installed 개가 OpenTypingPlus 손가락 레이어에 적용되었습니다."
