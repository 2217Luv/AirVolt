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
        <h2>设置</h2>
      </div>
      <div className="settings-content">
        <div className="setting-group">
          <div className="toggle-row">
            <span>开机自启</span>
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
          <label>刷新间隔</label>
          <select
            value={settings.refreshInterval}
            onChange={(e) => onSave({ refreshInterval: Number(e.target.value) })}
          >
            <option value={1}>每 1 分钟</option>
            <option value={5}>每 5 分钟</option>
            <option value={15}>每 15 分钟</option>
            <option value={30}>每 30 分钟</option>
          </select>
        </div>

        <div className="setting-group">
          <label>低电量阈值</label>
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
            <span>显示不支持的设备</span>
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
            <span>低电量通知</span>
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
