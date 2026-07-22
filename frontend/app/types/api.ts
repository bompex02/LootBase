export interface LeaderboardEntry {
  rank: number
  steamId64: string
  personaName: string
  avatarUrl?: string | null
  inventoryValue: number
  currency: string
  itemCount: number
  lastInventoryRefreshAt?: string | null
}

export interface InventoryItem {
  assetId: string
  marketHashName: string
  displayName: string
  iconUrl?: string | null
  type?: string | null
  exterior?: string | null
  rarity?: string | null
  quantity: number
  unitPrice: number
  totalPrice: number
  currency: string
}

export interface ItemMetadata {
  marketHashName: string
  displayName: string
  iconUrl?: string | null
  type?: string | null
  exterior?: string | null
  rarity?: string | null
}

export interface PlayerProfile {
  steamId64: string
  personaName: string
  avatarUrl?: string | null
  inventoryValue: number
  currency: string
  itemCount: number
  lastInventoryRefreshAt?: string | null
  topItems: InventoryItem[]
}

export interface PricingItem {
  marketHashName: string
  currency: string
  meanPrice?: number | null
  medianPrice?: number | null
  minPrice?: number | null
  maxPrice?: number | null
  itemPage?: string | null
  marketPage?: string | null
  source: string
  retrievedAt: string
}

export type PricingHistoryPeriodKey = '24h' | '7d' | '30d' | '90d'

export interface PricingHistoryPeriod {
  period: PricingHistoryPeriodKey
  minPrice?: number | null
  maxPrice?: number | null
  avgPrice?: number | null
  medianPrice?: number | null
  volume: number
}

export interface PricingHistoryDailyPoint {
  date: string
  minPrice?: number | null
  maxPrice?: number | null
  avgPrice?: number | null
  medianPrice?: number | null
  quantity: number
}

export interface PricingHistory {
  marketHashName: string
  currency: string
  periods: PricingHistoryPeriod[]
  dailyPoints: PricingHistoryDailyPoint[]
}
