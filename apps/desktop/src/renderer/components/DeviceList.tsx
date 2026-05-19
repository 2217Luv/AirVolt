import type { DeviceBatterySnapshot } from '../types'
import DeviceItem from './DeviceItem'

interface Props {
  devices: DeviceBatterySnapshot[]
}

export default function DeviceList({ devices }: Props) {
  if (devices.length === 0) {
    return (
      <div className="device-list">
        <div className="empty-state">
          <span className="empty-icon">🔋</span>
          <p>No devices found</p>
          <p style={{ fontSize: 11, marginTop: 4 }}>Click refresh to scan</p>
        </div>
      </div>
    )
  }

  return (
    <div className="device-list">
      {devices.map((device) => (
        <DeviceItem key={device.id} device={device} />
      ))}
    </div>
  )
}
