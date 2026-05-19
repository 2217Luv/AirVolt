import type { AppSettings } from '../types'

interface Props {
  settings: AppSettings
  onSave: (partial: Partial<AppSettings>) => void
  onBack: () => void
}

export default function SettingsPanel({ settings, onSave, onBack }: Props) {
  return (
    <div className="settings-panel">
      <div className="settings-header">
        <button className="back-btn" onClick={onBack}>←</button>
        <h2>Settings</h2>
      </div>
      <div className="settings-content">
        <div className="setting-group">
          <div className="toggle-row">
            <span>Start with Windows</span>
            <label className="toggle">
              <input
                type="checkbox"
                checked={settings.autoLaunch}
                onChange={(e) => onSave({ autoLaunch: e.target.checked })}
              />
              <span className="toggle-slider" />
            </label>
          </div>
        </div>

        <div className="setting-group">
          <label>Refresh Interval</label>
          <select
            value={settings.refreshInterval}
            onChange={(e) => onSave({ refreshInterval: Number(e.target.value) })}
          >
            <option value={1}>Every 1 minute</option>
            <option value={5}>Every 5 minutes</option>
            <option value={15}>Every 15 minutes</option>
            <option value={30}>Every 30 minutes</option>
          </select>
        </div>

        <div className="setting-group">
          <label>Low Battery Threshold</label>
          <select
            value={settings.lowBatteryThreshold}
            onChange={(e) => onSave({ lowBatteryThreshold: Number(e.target.value) })}
          >
            <option value={10}>10%</option>
            <option value={15}>15%</option>
            <option value={20}>20%</option>
            <option value={25}>25%</option>
          </select>
        </div>

        <div className="setting-group">
          <div className="toggle-row">
            <span>Show unsupported devices</span>
            <label className="toggle">
              <input
                type="checkbox"
                checked={settings.showUnsupported}
                onChange={(e) => onSave({ showUnsupported: e.target.checked })}
              />
              <span className="toggle-slider" />
            </label>
          </div>
        </div>

        <div className="setting-group">
          <div className="toggle-row">
            <span>Low battery notifications</span>
            <label className="toggle">
              <input
                type="checkbox"
                checked={settings.notificationsEnabled}
                onChange={(e) => onSave({ notificationsEnabled: e.target.checked })}
              />
              <span className="toggle-slider" />
            </label>
          </div>
        </div>
      </div>
    </div>
  )
}
