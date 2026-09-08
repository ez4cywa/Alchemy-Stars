param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$executable = Join-Path $publishPath 'AlchemyStars.Avalonia.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Avalonia executable was not found: $executable"
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$standardProject = Join-Path $repositoryRoot 'fork\AlchemyStars\Example\Hawk\HawkSprint.aprj'
$linkLog = Join-Path ([System.IO.Path]::GetTempPath()) ('AlchemyStars-about-links-' + [Guid]::NewGuid().ToString('N') + '.txt')
$arguments = '--accessibility-smoke --culture en-US --window-size 900x600 --page animations --dialog success --external-link-log "' + $linkLog + '" "' + $standardProject + '"'
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $publishPath -WindowStyle Hidden -PassThru
try {
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $window = $null
    do {
        Start-Sleep -Milliseconds 200
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
    } while ($null -eq $window -and [DateTime]::UtcNow -lt $deadline -and -not $process.HasExited)

    if ($null -eq $window) {
        throw 'Windows UI Automation could not discover the Avalonia main window.'
    }

    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $requiredButtons = @('New', 'Open', 'Save', 'Save as', 'Export all', 'Animations', 'Model parts', 'Settings', 'About', 'Close')
    $keyTargets = @('New', 'Open', 'Save', 'Save as', 'Export all', 'Close')
    $elements = @{}
    foreach ($name in $requiredButtons) {
        $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $name)
        $condition = [System.Windows.Automation.AndCondition]::new($buttonCondition, $nameCondition)
        $candidates = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        $element = $null
        for ($index = 0; $index -lt $candidates.Count; $index++) {
            $candidate = $candidates.Item($index)
            if ($candidate.Current.IsKeyboardFocusable) {
                $element = $candidate
                break
            }
        }
        if ($null -eq $element) {
            throw "Required accessible button was not exposed: $name"
        }
        $elements[$name] = $element
    }

    foreach ($name in $keyTargets) {
        $bounds = $elements[$name].Current.BoundingRectangle
        if ($bounds.Width -lt 43 -or $bounds.Height -lt 43) {
            throw "Accessible target '$name' is smaller than 44x44 DIPs: $($bounds.Width)x$($bounds.Height)"
        }
    }

    $trackNames = @(
        'Base animation, starts at frame 0, duration 1 frames',
        'sat_vm_ar_hawk_sprint_loop, starts at frame 0, duration 67 frames',
        'sat_vm_ar_hawk_sprint_offset_additive, starts at frame 0, duration 1 frames'
    )
    $trackElements = @{}
    foreach ($name in $trackNames) {
        $trackElement = $null
        do {
            $trackCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $name)
            $trackElement = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $trackCondition)
            if ($null -eq $trackElement) { Start-Sleep -Milliseconds 100 }
        } while ($null -eq $trackElement -and [DateTime]::UtcNow -lt $deadline)
        if ($null -eq $trackElement) {
            throw "Duration-aware track was not exposed to UI Automation: $name"
        }
        $trackElements[$name] = $trackElement
    }
    $baseBounds = $trackElements[$trackNames[0]].Current.BoundingRectangle
    $sprintBounds = $trackElements[$trackNames[1]].Current.BoundingRectangle
    $offsetBounds = $trackElements[$trackNames[2]].Current.BoundingRectangle
    if ($baseBounds.Width -lt 63 -or $offsetBounds.Width -lt 63) {
        throw 'One-frame animation tracks are smaller than the visible 64 DIP minimum.'
    }
    if ($sprintBounds.Width -le $baseBounds.Width * 2 -or $sprintBounds.Width -le $offsetBounds.Width * 2) {
        throw 'The 67-frame sprint track is not visibly longer than the one-frame tracks.'
    }

    $closeButton = $elements['Close']
    if (-not $closeButton.Current.HasKeyboardFocus) {
        $closeButton.SetFocus()
        Start-Sleep -Milliseconds 150
    }
    if (-not $closeButton.Current.HasKeyboardFocus) {
        throw 'The centered dialog did not provide a reliable keyboard focus target.'
    }

    ([System.Windows.Automation.InvokePattern]$closeButton.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 150
    ([System.Windows.Automation.InvokePattern]$elements['About'].GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()

    $aboutActions = @(
        @{ Name = 'Open the Alchemy Stars repository'; Url = 'https://github.com/ez4cywa/Alchemy-Stars' },
        @{ Name = 'Open upstream project'; Url = 'https://github.com/Scobalula/Alchemist' }
    )
    foreach ($action in $aboutActions) {
        $actionButton = $null
        do {
            $actionCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $action.Name)
            $actionButton = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.AndCondition]::new($buttonCondition, $actionCondition))
            if ($null -eq $actionButton) { Start-Sleep -Milliseconds 100 }
        } while ($null -eq $actionButton -and [DateTime]::UtcNow -lt $deadline)
        if ($null -eq $actionButton -or -not $actionButton.Current.IsKeyboardFocusable) {
            throw "The About action is missing or not keyboard operable: $($action.Name)"
        }
        $actionBounds = $actionButton.Current.BoundingRectangle
        if ($actionBounds.Height -lt 43) {
            throw "The About target is smaller than 44 DIPs: $($action.Name), $($actionBounds.Height)"
        }
        $actionButton.SetFocus()
        Start-Sleep -Milliseconds 100
        if (-not $actionButton.Current.HasKeyboardFocus) {
            throw "The About action could not receive keyboard focus: $($action.Name)"
        }
        if ($actionButton.Current.IsOffscreen) {
            throw "The About action stayed offscreen after receiving keyboard focus: $($action.Name)"
        }
        ([System.Windows.Automation.InvokePattern]$actionButton.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    }

    do {
        Start-Sleep -Milliseconds 100
        $openedLinks = if (Test-Path -LiteralPath $linkLog) { @(Get-Content -LiteralPath $linkLog) } else { @() }
    } while ($openedLinks.Count -lt $aboutActions.Count -and [DateTime]::UtcNow -lt $deadline)
    $expectedLinks = @($aboutActions | ForEach-Object Url)
    if ($openedLinks.Count -ne $expectedLinks.Count -or (Compare-Object $expectedLinks $openedLinks)) {
        throw "About actions did not route the expected external links: $($openedLinks -join ', ')"
    }

    Write-Output 'Windows UI Automation names, keyboard focus, 44x44 key targets, both About actions and duration-aware track geometry: PASS'
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
    $process.Dispose()
    if (Test-Path -LiteralPath $linkLog) {
        [System.IO.File]::Delete($linkLog)
    }
}
