interface Props {
  percentage: number
}

export default function BatteryMeter({ percentage }: Props) {
  const clamped = Math.max(0, Math.min(100, percentage))

  let level: 'high' | 'medium' | 'low'
  if (clamped > 50) level = 'high'
  else if (clamped > 20) level = 'medium'
  else level = 'low'

  return (
    <div className="battery-meter">
      <div
        className={`battery-fill ${level}`}
        style={{ width: `${clamped}%` }}
      />
    </div>
  )
}
