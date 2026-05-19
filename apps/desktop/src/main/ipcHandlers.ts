import { ipcMain } from 'electron'
import { getSettings, saveSettings } from './settingsStore'
import type { DeviceBatterySnapshot, AppSettings } from '../renderer/types'

let cachedDevices: DeviceBatterySnapshot[] = []
let broadcastCb: (() => void) | null = null

export function setBroadcastCallback(cb: () => void): void {
  broadcastCb = cb
}

export function getCachedDevices(): DeviceBatterySnapshot[] {
  return cachedDevices
}

export function setCachedDevices(devices: DeviceBatterySnapshot[]): void {
  cachedDevices = devices
}

export function registerIpcHandlers(): void {
  ipcMain.handle('getDevices', () => cachedDevices)

  ipcMain.handle('refreshDevices', async () => {
    broadcastCb?.()
    return cachedDevices
  })

  ipcMain.handle('getSettings', () => getSettings())

  ipcMain.handle('saveSettings', (_event, partial: Partial<AppSettings>) => {
    return saveSettings(partial)
  })

  let helperStatus = { connected: false, version: null as string | null }
  ipcMain.handle('getHelperStatus', () => helperStatus)

  ipcMain.on('openSettingsWindow', () => {
    const { openSettingsWindow } = require('./tray')
    openSettingsWindow()
  })

  ipcMain.handle('updateHelperStatus', (_event, status: { connected: boolean; version: string | null }) => {
    helperStatus = status
  })
}
