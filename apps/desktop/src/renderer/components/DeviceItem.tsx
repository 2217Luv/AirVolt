import type { DeviceBatterySnapshot } from '../types'
import BatteryMeter from './BatteryMeter'
import StatusBadge from './StatusBadge'

const DEVICE_ICONS: Record<string, string> = {
  mouse: '🖱',
  keyboard: '⌨',
  headset: '🎧',
  controller: '🎮',
  pen: '✒',
  unknown: '📡'
}

const CONNECTION_LABELS: Record<string, string> = {
  'bluetoothLE': 'BLE',
  'bluetoothClassic': 'BT',
  'usb24G': '2.4G',
  'usb': 'USB',
  'unknown': '?'
}

interface Props {
  device: DeviceBatterySnapshot
}

export default function DeviceItem({ device }: Props) {
  const icon = DEVICE_ICONS[device.kind] ?? DEVICE_ICONS.unknown
  const connLabel = CONNECTION_LABELS[device.connection] ?? '?'
  const hasBattery = device.battery.percentage !== null && device.battery.status === 'available'

  return (
    <div className="device-item">
      <div className="device-icon">{icon}</div>
      <div className="device-info">
        <div className="device-name">{device.name}</div>
        <div className="device-meta">
          <span className="connection-tag">{connLabel}</span>
          <StatusBadge status={device.battery.status} />
        </div>
      </div>
      <div className="device-battery">
        {hasBattery ? (
          <>
            <BatteryMeter percentage={device.battery.percentage!} />
            <span className="battery-percentage">
              {device.battery.percentage}%
            </span>
          </>
        ) : (
          <span className="battery-percentage unsupported">
            {device.battery.status === 'unsupported' ? 'N/A' : '--'}
          </span>
        )}
      </div>
    </div>
  )
}
