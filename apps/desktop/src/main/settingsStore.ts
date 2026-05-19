import Store from 'electron-store'
import type { AppSettings } from '../renderer/types'
import { DEFAULT_SETTINGS } from '../renderer/types'

const store = new Store<AppSettings>({
  name: 'settings',
  defaults: DEFAULT_SETTINGS
})

export function getSettings(): AppSettings {
  return {
    autoLaunch: store.get('autoLaunch'),
    refreshInterval: store.get('refreshInterval'),
    lowBatteryThreshold: store.get('lowBatteryThreshold'),
    showUnsupported: store.get('showUnsupported'),
    notificationsEnabled: store.get('notificationsEnabled')
  }
}

export function saveSettings(partial: Partial<AppSettings>): AppSettings {
  for (const [key, value] of Object.entries(partial)) {
    if (value !== undefined) {
      store.set(key as keyof AppSettings, value as never)
    }
  }
  return getSettings()
}

export function onSettingsChange(callback: (settings: AppSettings) => void): void {
  store.onDidAnyChange(() => {
    callback(getSettings())
  })
}
