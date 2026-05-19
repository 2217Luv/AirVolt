import type { BatteryStatus } from '../types'

const STATUS_LABELS: Record<BatteryStatus, string> = {
  available: 'OK',
  unsupported: 'No data',
  unknown: 'Unknown',
  error: 'Error'
}

interface Props {
  status: BatteryStatus
}

export default function StatusBadge({ status }: Props) {
  return (
    <span className={`status-badge ${status}`}>
      {STATUS_LABELS[status]}
    </span>
  )
}
