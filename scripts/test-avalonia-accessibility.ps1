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
$arguments = '--accessibility-smoke --culture en-US --window-size 900x600 --page animations --dialog success "' + $standardProject + '"'
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

    $repositoryName = 'Open the Alchemy Stars repository'
    $repositoryButton = $null
    do {
        $repositoryCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $repositoryName)
        $repositoryButton = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.AndCondition]::new($buttonCondition, $repositoryCondition))
        if ($null -eq $repositoryButton) { Start-Sleep -Milliseconds 100 }
    } while ($null -eq $repositoryButton -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $repositoryButton -or -not $repositoryButton.Current.IsKeyboardFocusable) {
        throw 'The About project repository action is missing or not keyboard operable.'
    }
    $repositoryBounds = $repositoryButton.Current.BoundingRectangle
    if ($repositoryBounds.Height -lt 43) {
        throw "The About project repository target is smaller than 44 DIPs: $($repositoryBounds.Height)"
    }
    $repositoryButton.SetFocus()
    Start-Sleep -Milliseconds 150
    if (-not $repositoryButton.Current.HasKeyboardFocus) {
        throw 'The About project repository action could not receive keyboard focus.'
    }

    Write-Output 'Windows UI Automation names, keyboard focus, 44x44 key targets, About repository action and duration-aware track geometry: PASS'
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
    $process.Dispose()
}
