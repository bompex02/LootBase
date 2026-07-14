<template>
  <div class="space-y-3">
    <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-zinc-500">
      <span class="flex items-center gap-1.5">
        <span class="inline-block h-0.5 w-3 rounded-full" :style="{ backgroundColor: color }" />
        Median
      </span>
      <span class="flex items-center gap-1.5">
        <span class="inline-block h-0.5 w-3 rounded-full bg-zinc-500" />
        Ø Durchschnitt
      </span>
      <span class="flex items-center gap-1.5">
        <span class="inline-block h-2.5 w-3 rounded-sm" :style="{ backgroundColor: color, opacity: 0.18 }" />
        Min&ndash;Max Bereich
      </span>
    </div>

    <div v-if="points.length < 2" class="text-sm text-zinc-500">
      Für diesen Zeitraum liegen nicht genug Daten für einen Verlauf vor.
    </div>

    <ClientOnly v-else>
      <div class="relative">
        <svg :viewBox="`0 0 ${width} ${height}`" class="w-full" role="img" :aria-label="`Preisverlauf für ${currency}`">
          <g>
            <line
              v-for="grid in gridLines"
              :key="grid.value"
              :x1="padding.left"
              :x2="width - padding.right"
              :y1="grid.y"
              :y2="grid.y"
              stroke="#27272a"
              stroke-width="1"
            />
          </g>
          <g>
            <text
              v-for="grid in gridLines"
              :key="`label-${grid.value}`"
              :x="padding.left - 8"
              :y="grid.y"
              text-anchor="end"
              dominant-baseline="middle"
              class="fill-zinc-500"
              font-size="9"
            >{{ formatCurrency(grid.value, currency) }}</text>
          </g>

          <path :d="bandPath" :fill="color" :opacity="0.18" stroke="none" />

          <path :d="linePath(points.map(p => p.avg))" fill="none" stroke="#71717a" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />

          <path :d="linePath(points.map(p => p.median))" fill="none" :stroke="color" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />

          <g>
            <circle
              v-for="marker in markers"
              :key="marker.key"
              :cx="marker.x"
              :cy="marker.y"
              r="4"
              :fill="color"
              stroke="#101821"
              stroke-width="2"
            />
            <circle
              v-if="hoveredMarker"
              :cx="hoveredMarker.x"
              :cy="hoveredMarker.y"
              r="4"
              :fill="color"
              stroke="#101821"
              stroke-width="2"
            />
          </g>

          <text
            v-if="lastPoint?.median != null"
            :x="xFor(points.length - 1)"
            :y="yFor(lastPoint.median) - 10"
            text-anchor="end"
            class="fill-zinc-100"
            font-size="11"
            font-weight="600"
          >{{ formatCurrency(lastPoint.median, currency) }}</text>

          <g>
            <template v-for="{ index, point } in labeledPoints" :key="`x-label-${point.key}`">
              <text
                :x="xFor(index)"
                :y="height - padding.bottom + 16"
                :text-anchor="labelAnchor(index)"
                class="fill-zinc-500"
                font-size="10"
              >{{ point.label }}</text>
              <text
                v-if="showVolumeLabels"
                :x="xFor(index)"
                :y="height - padding.bottom + 29"
                :text-anchor="labelAnchor(index)"
                class="fill-zinc-600"
                font-size="9"
              >{{ point.volume }}x verkauft</text>
            </template>
          </g>

          <rect
            :x="padding.left"
            :y="padding.top"
            :width="plotWidth"
            :height="plotHeight"
            fill="transparent"
            class="cursor-crosshair"
            @mousemove="onPlotMouseMove"
            @mouseleave="hoveredIndex = null"
          />
        </svg>

        <div
          v-if="hoveredPoint"
          class="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded-md border border-zinc-800 bg-[#0b0f14] px-3 py-2 text-xs shadow-lg"
          :style="{ left: `${hoveredXPercent}%` }"
        >
          <p class="mb-1 font-medium text-zinc-100">{{ hoveredPoint.label }}</p>
          <dl class="grid grid-cols-[auto_auto] gap-x-3 gap-y-0.5 text-zinc-400">
            <dt>Min</dt><dd class="text-right text-zinc-200">{{ formatOrDash(hoveredPoint.min) }}</dd>
            <dt>Median</dt><dd class="text-right text-zinc-200">{{ formatOrDash(hoveredPoint.median) }}</dd>
            <dt>Ø Avg</dt><dd class="text-right text-zinc-200">{{ formatOrDash(hoveredPoint.avg) }}</dd>
            <dt>Max</dt><dd class="text-right text-zinc-200">{{ formatOrDash(hoveredPoint.max) }}</dd>
            <dt>Verkäufe</dt><dd class="text-right text-zinc-200">{{ hoveredPoint.volume }}</dd>
          </dl>
        </div>
      </div>
      <template #fallback>
        <div class="h-50 animate-pulse rounded-md bg-zinc-900" />
      </template>
    </ClientOnly>

    <details class="text-xs text-zinc-500">
      <summary class="cursor-pointer select-none">Als Tabelle anzeigen</summary>
      <table class="mt-2 w-full border-collapse text-left">
        <thead>
          <tr class="text-zinc-500">
            <th class="py-1 pr-3 font-normal">Zeitraum</th>
            <th class="py-1 pr-3 font-normal">Min</th>
            <th class="py-1 pr-3 font-normal">Median</th>
            <th class="py-1 pr-3 font-normal">Ø Avg</th>
            <th class="py-1 pr-3 font-normal">Max</th>
            <th class="py-1 font-normal">Verkäufe</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="point in points" :key="point.key" class="border-t border-zinc-800 text-zinc-300">
            <td class="py-1 pr-3">{{ point.label }}</td>
            <td class="py-1 pr-3">{{ formatOrDash(point.min) }}</td>
            <td class="py-1 pr-3">{{ formatOrDash(point.median) }}</td>
            <td class="py-1 pr-3">{{ formatOrDash(point.avg) }}</td>
            <td class="py-1 pr-3">{{ formatOrDash(point.max) }}</td>
            <td class="py-1">{{ point.volume }}</td>
          </tr>
        </tbody>
      </table>
    </details>
  </div>
</template>

<script setup lang="ts">
export interface PriceHistoryChartPoint {
  key: string
  label: string
  min: number | null
  max: number | null
  avg: number | null
  median: number | null
  volume: number
}

const props = defineProps<{
  points: PriceHistoryChartPoint[]
  currency: string
  color: string
}>()

const width = 600
const height = 200
const padding = { top: 28, right: 16, bottom: 44, left: 52 }
const plotWidth = width - padding.left - padding.right
const plotHeight = height - padding.top - padding.bottom

const formatOrDash = (value: number | null) => value === null ? '–' : formatCurrency(value, props.currency)

const points = computed(() => props.points)
const lastPoint = computed(() => points.value.at(-1) ?? null)

const MAX_LABELS = 6
const labeledIndices = computed(() => {
  const total = points.value.length
  if (total <= MAX_LABELS) {
    return Array.from({ length: total }, (_, i) => i)
  }

  const step = (total - 1) / (MAX_LABELS - 1)
  const indices = new Set<number>()
  for (let i = 0; i < MAX_LABELS; i++) {
    indices.add(Math.round(i * step))
  }
  return Array.from(indices).sort((a, b) => a - b)
})

const labeledPoints = computed(() => labeledIndices.value
  .map(index => ({ index, point: points.value[index] }))
  .filter((entry): entry is { index: number, point: PriceHistoryChartPoint } => entry.point !== undefined))

const showVolumeLabels = computed(() => points.value.length <= MAX_LABELS)

const markers = computed(() => labeledPoints.value
  .filter(({ point }) => point.median !== null)
  .map(({ index, point }) => ({
    key: point.key,
    x: xFor(index),
    y: yFor(point.median as number)
  })))

const hoveredMarker = computed(() => {
  const index = hoveredIndex.value
  if (index === null || labeledIndices.value.includes(index)) {
    return null
  }

  const point = points.value[index]
  if (!point || point.median === null) {
    return null
  }

  return { key: point.key, x: xFor(index), y: yFor(point.median) }
})

const robustValues = computed(() =>
  points.value
    .flatMap(point => [point.median, point.avg])
    .filter((value): value is number => typeof value === 'number'))

const yDomain = computed(() => {
  if (robustValues.value.length === 0) {
    return { min: 0, max: 1 }
  }

  const min = Math.min(...robustValues.value)
  const max = Math.max(...robustValues.value)
  const pad = (max - min) * 0.25 || max * 0.15 || 1

  return { min: Math.max(0, min - pad), max: max + pad }
})

const xFor = (index: number) => {
  const total = points.value.length
  return total <= 1 ? padding.left + plotWidth / 2 : padding.left + (plotWidth * index) / (total - 1)
}

const yFor = (value: number) => {
  const { min, max } = yDomain.value
  const ratio = max === min ? 0.5 : (value - min) / (max - min)
  const clampedRatio = Math.min(1, Math.max(0, ratio))
  return padding.top + plotHeight - clampedRatio * plotHeight
}

const labelAnchor = (index: number): 'start' | 'middle' | 'end' => {
  if (index === 0) {
    return 'start'
  }
  return index === points.value.length - 1 ? 'end' : 'middle'
}

const linePath = (values: (number | null)[]) => {
  const segments: string[] = []
  values.forEach((value, index) => {
    if (value === null) {
      return
    }
    segments.push(`${segments.length === 0 ? 'M' : 'L'} ${xFor(index)} ${yFor(value)}`)
  })
  return segments.join(' ')
}

const bandPath = computed(() => {
  const pts = points.value
  if (pts.length < 2) {
    return ''
  }

  const top = pts.map((point, index) => {
    const value = point.max ?? point.median
    return value === null ? null : `${index === 0 ? 'M' : 'L'} ${xFor(index)} ${yFor(value)}`
  }).filter(Boolean)

  const bottom = [...pts].reverse().map((point, reversedIndex) => {
    const index = pts.length - 1 - reversedIndex
    const value = point.min ?? point.median
    return value === null ? null : `L ${xFor(index)} ${yFor(value)}`
  }).filter(Boolean)

  return [...top, ...bottom, 'Z'].join(' ')
})

const gridLines = computed(() => {
  const { min, max } = yDomain.value
  const steps = 4
  return Array.from({ length: steps + 1 }, (_, i) => {
    const value = min + ((max - min) * i) / steps
    return { value, y: yFor(value) }
  })
})

const hoveredIndex = ref<number | null>(null)
const hoveredPoint = computed(() => hoveredIndex.value === null ? null : points.value[hoveredIndex.value])
const hoveredXPercent = computed(() => hoveredIndex.value === null ? 0 : (xFor(hoveredIndex.value) / width) * 100)

const onPlotMouseMove = (event: MouseEvent) => {
  const total = points.value.length
  if (total === 0) {
    return
  }

  const svg = (event.currentTarget as SVGElement).ownerSVGElement ?? (event.currentTarget as unknown as SVGSVGElement)
  const rect = svg.getBoundingClientRect()
  const svgX = ((event.clientX - rect.left) / rect.width) * width

  if (total === 1) {
    hoveredIndex.value = 0
    return
  }

  const ratio = (svgX - padding.left) / plotWidth
  const nearest = Math.round(ratio * (total - 1))
  hoveredIndex.value = Math.min(total - 1, Math.max(0, nearest))
}
</script>