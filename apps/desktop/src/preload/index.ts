import { contextBridge, ipcRenderer } from 'electron'
import type { DeviceBatterySnapshot, AppSettings } from '../renderer/types'

const api = {
  getDevices: (): Promise<DeviceBatterySnapshot[]> =>
    ipcRenderer.invoke('getDevices'),

  refreshDevices: (): Promise<DeviceBatterySnapshot[]> =>
    ipcRenderer.invoke('refreshDevices'),

  getSettings: (): Promise<AppSettings> =>
    ipcRenderer.invoke('getSettings'),

  saveSettings: (settings: Partial<AppSettings>): Promise<AppSettings> =>
    ipcRenderer.invoke('saveSettings', settings),

  getHelperStatus: (): Promise<{ connected: boolean; version: string | null }> =>
    ipcRenderer.invoke('getHelperStatus'),

  openSettingsWindow: (): void =>
    ipcRenderer.send('openSettingsWindow'),

  onDevicesUpdated: (callback: (devices: DeviceBatterySnapshot[]) => void) => {
    const handler = (_event: Electron.IpcRendererEvent, devices: DeviceBatterySnapshot[]) =>
      callback(devices)
    ipcRenderer.on('devicesUpdated', handler)
    return () => {
      ipcRenderer.removeListener('devicesUpdated', handler)
    }
  }
}

contextBridge.exposeInMainWorld('airvolt', api)

export type AirVoltAPI = typeof api
