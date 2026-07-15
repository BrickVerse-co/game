declare json: {
    parse: ((string?) -> any),
    serialize: ((any?) -> string),
    null: (() -> string),
    isNull: ((string?) -> boolean),
}

declare guid: {
    new: (() -> string),
    empty: (() -> string),
}

declare _BV_VERSION: string

declare _BV_2: boolean

declare _LOCALTEST: boolean

declare function wait(seconds: number?)
    
declare function spawn(callback: () -> ())

declare function tick(): number

declare function time(): number

declare function require(moduleScript: (ModuleScript)): any

declare function pcall(f: (...any) -> (boolean, any))

declare world: World

declare script: Script

declare Environment: Environment

declare Lighting: Lighting

declare Players: Players

declare ScriptService: ScriptService

declare Hidden: Hidden

declare ServerHidden: ServerHidden

declare PlayerDefaults: PlayerDefaults

declare PlayerGUI: PlayerGUI

declare Chat: ChatService

declare Filter: FilterService

declare Camera: Camera

declare Assets: AssetsService

declare Input: InputService

declare Achievements: AchievementsService

declare Tween: TweenService

declare CoreUI: CoreUIService

declare Stats: Stats

declare Teams: Teams

declare Datastore: DatastoreService

declare Http: HttpService

declare Insert: InsertService

declare Purchases: PurchasesService

declare Capture: CaptureService

declare Presence: PresenceService

declare Creator: CreatorService

declare CreatorContext: CreatorContextService

declare IO: IOService

declare Addons: CreatorAddons

declare Selections: CreatorSelections

declare History: CreatorHistory

declare CreatorGUI: CreatorGUI

declare Preferences: PreferencesService

declare class Terrain extends Instance
	-- Persisted terrain data
	SerialisedTerrain: string

	-- Settings
	AutoSerialise: boolean
	GenerateCollisions: boolean
	DefaultSdfStrength: number
	DefaultSdfScale: number
	ViewDistance: number

	-- Shapes
	FillBall: (self: Terrain, center: Vector3, radius: number, material: number?) -> ()
	DigBall: (self: Terrain, center: Vector3, radius: number) -> ()

	FillBlock: (self: Terrain, center: Vector3, size: Vector3, material: number?) -> ()
	DigBlock: (self: Terrain, center: Vector3, size: Vector3) -> ()

	FillCylinder: (
		self: Terrain,
		center: Vector3,
		height: number,
		radius: number,
		material: number?
	) -> ()

	DigCylinder: (
		self: Terrain,
		center: Vector3,
		height: number,
		radius: number
	) -> ()

	-- Painting
	PaintBall: (
		self: Terrain,
		center: Vector3,
		radius: number,
		material: number,
		opacity: number?
	) -> ()

	SmoothBall: (
		self: Terrain,
		center: Vector3,
		radius: number,
		blurRadius: number?
	) -> ()

	GrowBall: (
		self: Terrain,
		center: Vector3,
		radius: number,
		strength: number?
	) -> ()

	ErodeBall: (
		self: Terrain,
		center: Vector3,
		radius: number,
		strength: number?
	) -> ()

	-- Individual voxels
	SetVoxelSdf: (
		self: Terrain,
		position: Vector3,
		sdf: number
	) -> ()

	GetVoxelSdf: (
		self: Terrain,
		position: Vector3
	) -> number

	SetVoxelMaterial: (
		self: Terrain,
		position: Vector3,
		material: number
	) -> ()

	GetVoxelMaterial: (
		self: Terrain,
		position: Vector3
	) -> number

	SetVoxelMetadata: (
		self: Terrain,
		position: Vector3,
		metadata: any
	) -> ()

	GetVoxelMetadata: (
		self: Terrain,
		position: Vector3
	) -> any

	-- Persistence
	SaveTerrain: (self: Terrain) -> string
	LoadSerialisedTerrain: (self: Terrain) -> ()
	Clear: (self: Terrain) -> ()

	-- Utilities
	IsAreaEditable: (
		self: Terrain,
		minimum: Vector3,
		maximum: Vector3
	) -> boolean

	CountOperations: (self: Terrain) -> number
end

declare Terrain: Terrain