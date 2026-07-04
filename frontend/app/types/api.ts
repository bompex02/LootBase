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
  suggestedPrice?: number | null
  meanPrice?: number | null
  medianPrice?: number | null
  minPrice?: number | null
  maxPrice?: number | null
  quantity: number
  itemPage?: string | null
  marketPage?: string | null
  source: string
  retrievedAt: string
  updatedAt?: string | null
}
