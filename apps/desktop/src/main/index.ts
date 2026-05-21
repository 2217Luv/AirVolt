import { app, BrowserWindow, Notification } from 'electron'
import { join } from 'path'
import { createTray, closePopup } from './tray'
import { HelperBridge } from './helperBridge'
import {
  registerIpcHandlers,
  setCachedDevices,
  getCachedDevices,
  setBroadcastCallback
} from './ipcHandlers'
import { getSettings } from './settingsStore'
import type { DeviceBatterySnapshot } from '../renderer/types'

let helper: HelperBridge | null = null
let refreshTimer: ReturnType<typeof setInterval> | null = null
let notifiedDevices = new Set<string>()

function createHiddenWindow(): BrowserWindow {
  const win = new BrowserWindow({
    width: 1,
    height: 1,
    show: false,
    skipTaskbar: true,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false
    }
  })
  win.hide()
  return win
}

function getHelperProjectPath(): string {
  return join(app.getAppPath(), '..', '..', 'native', 'AirVolt.NativeHelper')
}

async function scanAndBroadcast(): Promise<void> {
  try {
    if (!helper) return
    const devices = await helper.scanDevices()
    const settings = getSettings()

    const filtered = settings.showUnsupported
      ? devices
      : devices.filter(d => d.battery.status !== 'unsupported')

    setCachedDevices(filtered)
    checkLowBattery(filtered, settings.lowBatteryThreshold)

    // Broadcast to ALL open windows, not just the first one
    for (const win of BrowserWindow.getAllWindows()) {
      if (!win.isDestroyed()) {
        win.webContents.send('devicesUpdated', filtered)
      }
    }
  } catch (err) {
    console.error('[Main] scan failed:', err)
  }
}

function checkLowBattery(devices: DeviceBatterySnapshot[], threshold: number): void {
  const settings = getSettings()
  if (!settings.notificationsEnabled) return

  for (const device of devices) {
    if (
      device.battery.percentage !== null &&
      device.battery.percentage <= threshold &&
      device.battery.status === 'available'
    ) {
      const notifyKey = `${device.id}-${device.battery.percentage}`
      if (!notifiedDevices.has(notifyKey)) {
        notifiedDevices.add(notifyKey)
        new Notification({
          title: 'AirVolt - 低电量提醒',
          body: `${device.name} 电量仅剩 ${device.battery.percentage}%`,
          icon: join(__dirname, '../../resources/icon.png')
        }).show()
      }
    }
  }
}

function setupRefreshTimer(): void {
  if (refreshTimer) clearInterval(refreshTimer)
  const settings = getSettings()
  refreshTimer = setInterval(
    () => scanAndBroadcast(),
    settings.refreshInterval * 60 * 1000
  )
}

function setupHelper(): void {
  const helperPath = getHelperProjectPath()
  helper = new HelperBridge(helperPath)

  helper.setStatusCallback((connected) => {
    console.log(`[Main] helper connected: ${connected}`)
  })

  helper.on('devices.changed', (payload) => {
    const { devices } = payload as { devices: DeviceBatterySnapshot[] }
    setCachedDevices(devices)
    for (const win of BrowserWindow.getAllWindows()) {
      if (!win.isDestroyed()) {
        win.webContents.send('devicesUpdated', devices)
      }
    }
  })

  helper.start()

  setTimeout(async () => {
    try {
      const health = await helper!.healthCheck()
      console.log(`[Main] helper health check OK, version=${health.version}`)
      await scanAndBroadcast()
    } catch (err) {
      console.error('[Main] initial health check failed:', err)
    }
  }, 2000)
}

app.whenReady().then(() => {
  registerIpcHandlers()

  const hiddenWin = createHiddenWindow()

  setBroadcastCallback(() => scanAndBroadcast())

  const tray = createTray(() => scanAndBroadcast())
  setupHelper()
  setupRefreshTimer()
})

app.on('window-all-closed', () => {
  // Don't quit; tray app stays running
})

app.on('before-quit', () => {
  if (refreshTimer) clearInterval(refreshTimer)
  helper?.stop()
  closePopup()
})
