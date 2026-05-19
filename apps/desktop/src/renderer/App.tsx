import { useState, useEffect } from 'react'
import type { DeviceBatterySnapshot, AppSettings } from './types'
import { DEFAULT_SETTINGS } from './types'
import DeviceList from './components/DeviceList'
import SettingsPanel from './components/SettingsPanel'

declare global {
  interface Window {
    airvolt: import('../preload/index').AirVoltAPI
  }
}

export default function App() {
  const [view, setView] = useState<'devices' | 'settings'>('devices')
  const [devices, setDevices] = useState<DeviceBatterySnapshot[]>([])
  const [settings, setSettings] = useState<AppSettings>(DEFAULT_SETTINGS)
  const [refreshing, setRefreshing] = useState(false)

  useEffect(() => {
    const isSettings = window.location.hash === '#settings'
    setView(isSettings ? 'settings' : 'devices')

    const handleHashChange = () => {
      setView(window.location.hash === '#settings' ? 'settings' : 'devices')
    }
    window.addEventListener('hashchange', handleHashChange)

    loadInitialData()

    const unsubscribe = window.airvolt.onDevicesUpdated((newDevices) => {
      setDevices(newDevices)
    })

    return () => {
      window.removeEventListener('hashchange', handleHashChange)
      unsubscribe()
    }
  }, [])

  async function loadInitialData() {
    try {
      const [devices, settings] = await Promise.all([
        window.airvolt.getDevices(),
        window.airvolt.getSettings()
      ])
      setDevices(devices)
      setSettings(settings)
    } catch (err) {
      console.error('Failed to load initial data:', err)
    }
  }

  async function handleRefresh() {
    setRefreshing(true)
    try {
      const newDevices = await window.airvolt.refreshDevices()
      setDevices(newDevices)
    } finally {
      setRefreshing(false)
    }
  }

  async function handleSaveSettings(partial: Partial<AppSettings>) {
    try {
      const updated = await window.airvolt.saveSettings(partial)
      setSettings(updated)
    } catch (err) {
      console.error('Failed to save settings:', err)
    }
  }

  if (view === 'settings') {
    return (
      <SettingsPanel
        settings={settings}
        onSave={handleSaveSettings}
        onBack={() => setView('devices')}
      />
    )
  }

  return (
    <div className="app">
      <header className="app-header">
        <h1 className="app-title">AirVolt</h1>
        <div className="header-actions">
          <button
            className="icon-btn"
            onClick={handleRefresh}
            disabled={refreshing}
            title="Refresh"
          >
            {refreshing ? '⟳' : '↻'}
          </button>
          <button
            className="icon-btn"
            onClick={() => window.airvolt.openSettingsWindow()}
            title="Settings"
          >
            ⚙
          </button>
        </div>
      </header>
      <DeviceList devices={devices} />
      <footer className="app-footer">
        <span>{devices.length} device{devices.length !== 1 ? 's' : ''}</span>
      </footer>
    </div>
  )
}
