export type DeviceKind = "mouse" | "keyboard" | "headset" | "controller" | "pen" | "unknown"
export type DeviceConnection = "bluetoothLE" | "bluetoothClassic" | "usb24G" | "usb" | "unknown"
export type BatteryStatus = "available" | "unsupported" | "unknown" | "error"
export type Provider = "bluetooth-bas" | "windows-device-property" | "hid" | "vendor-logitech" | "cache" | "mock"

export interface DeviceBatterySnapshot {
  id: string
  name: string
  kind: DeviceKind
  connection: DeviceConnection
  battery: {
    percentage: number | null
    status: BatteryStatus
    charging?: boolean | null
    levelText?: "low" | "medium" | "high" | null
  }
  provider: Provider
  lastSeenAt: string
  updatedAt: string | null
  error?: {
    code: string
    message: string
  }
}

export interface AppSettings {
  autoLaunch: boolean
  refreshInterval: number
  lowBatteryThreshold: number
  showUnsupported: boolean
  notificationsEnabled: boolean
}

export const DEFAULT_SETTINGS: AppSettings = {
  autoLaunch: false,
  refreshInterval: 5,
  lowBatteryThreshold: 15,
  showUnsupported: true,
  notificationsEnabled: true
}
