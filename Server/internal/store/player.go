package store

import (
	"context"
	"errors"
	"strings"
	"time"

	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"prometheus/internal/item"
	"prometheus/internal/poi"
	"prometheus/internal/room"
)

// PlayerAccount 保存玩家账号基础信息；账号认证尚未接入时允许字段为空。
type PlayerAccount struct {
	Platform  string    `bson:"platform,omitempty"`
	UID       string    `bson:"uid,omitempty"`
	Created   time.Time `bson:"created_at"`
	LastLogin time.Time `bson:"last_login_at"`
}

// PlayerDocument 是玩家聚合根：一个玩家的账号、背包和个人 POI 状态全部存于同一个 Mongo 文档。
// POI 的静态定义仍来自策划导出，文档中保存的是该玩家当前可变状态及其同步所需定义快照。
type PlayerDocument struct {
	ID            string         `bson:"_id"`
	SchemaVersion int32          `bson:"schema_version"`
	Account       PlayerAccount  `bson:"account"`
	Inventory     []*item.Stack  `bson:"inventory"`
	POIs          []*poi.Poi     `bson:"pois"`
	Position      *room.Position `bson:"position,omitempty"`
	Revision      int64          `bson:"revision"`
	CreatedAt     time.Time      `bson:"created_at"`
	UpdatedAt     time.Time      `bson:"updated_at"`
}

// MongoPlayerStore 负责 players 集合的连接和玩家聚合文档读写。
type MongoPlayerStore struct {
	coll          *mongo.Collection
	db            *mongo.Database
	defaultPlayer string
}

// NewMongoPlayerStore 创建玩家聚合存储，并为玩家账号平台与 UID建立唯一索引。
func NewMongoPlayerStore(ctx context.Context, uri, db, coll, defaultPlayer string) (*MongoPlayerStore, error) {
	client, err := mongo.Connect(ctx, options.Client().ApplyURI(uri).SetServerSelectionTimeout(5*time.Second))
	if err != nil {
		return nil, err
	}
	if err := client.Ping(ctx, nil); err != nil {
		return nil, err
	}
	database := client.Database(db)
	collection := database.Collection(coll)
	if _, err := collection.Indexes().CreateOne(ctx, mongo.IndexModel{Keys: bson.D{{Key: "account.platform", Value: 1}, {Key: "account.uid", Value: 1}}, Options: options.Index().SetUnique(true).SetSparse(true)}); err != nil {
		return nil, err
	}
	return &MongoPlayerStore{coll: collection, db: database, defaultPlayer: defaultPlayer}, nil
}

// LoadPlayer 读取玩家聚合文档；不存在时创建一个空玩家文档，保证后续更新始终有明确根节点。
func (s *MongoPlayerStore) LoadPlayer(ctx context.Context, playerID string) (*PlayerDocument, error) {
	if playerID == "" {
		playerID = s.defaultPlayer
	}
	var document PlayerDocument
	err := s.coll.FindOne(ctx, bson.M{"_id": playerID}).Decode(&document)
	if errors.Is(err, mongo.ErrNoDocuments) {
		now := time.Now().UTC()
		document = PlayerDocument{ID: playerID, SchemaVersion: 1, Account: PlayerAccount{UID: playerID, Created: now, LastLogin: now}, Inventory: make([]*item.Stack, 0), POIs: make([]*poi.Poi, 0), CreatedAt: now, UpdatedAt: now}
		if _, err := s.coll.InsertOne(ctx, document); err != nil && !mongo.IsDuplicateKeyError(err) {
			return nil, err
		}
		if err := s.coll.FindOne(ctx, bson.M{"_id": playerID}).Decode(&document); err != nil {
			return nil, err
		}
	}
	return &document, err
}

// savePlayer 写回整个玩家聚合文档；当前数据规模较小，整文档替换可保证账号、背包和 POI 快照一致。
func (s *MongoPlayerStore) savePlayer(ctx context.Context, document *PlayerDocument) error {
	document.Revision++
	document.UpdatedAt = time.Now().UTC()
	_, err := s.coll.ReplaceOne(ctx, bson.M{"_id": document.ID}, document, options.Replace().SetUpsert(true))
	return err
}

// LoadInventory 读取玩家背包快照。
func (s *MongoPlayerStore) LoadInventory(ctx context.Context, playerID string) ([]*item.Stack, error) {
	document, err := s.LoadPlayer(ctx, playerID)
	if err != nil {
		return nil, err
	}
	return document.Inventory, nil
}

// UpsertInventoryStack 更新玩家背包中的一叠物品，并保存整个玩家聚合文档。
func (s *MongoPlayerStore) UpsertInventoryStack(ctx context.Context, stack *item.Stack) error {
	document, err := s.LoadPlayer(ctx, stack.PlayerID)
	if err != nil {
		return err
	}
	for index, existing := range document.Inventory {
		if existing.ItemID == stack.ItemID && existing.Quality == stack.Quality {
			if stack.Quantity <= 0 {
				document.Inventory = append(document.Inventory[:index], document.Inventory[index+1:]...)
			} else {
				document.Inventory[index] = stack
			}
			return s.savePlayer(ctx, document)
		}
	}
	if stack.Quantity <= 0 {
		return nil
	}
	document.Inventory = append(document.Inventory, stack)
	return s.savePlayer(ctx, document)
}

// LoadPlayerPOIs 读取玩家个人 POI 快照。
func (s *MongoPlayerStore) LoadPlayerPOIs(ctx context.Context, playerID string) ([]*poi.Poi, error) {
	document, err := s.LoadPlayer(ctx, playerID)
	if err != nil {
		return nil, err
	}
	return document.POIs, nil
}

// UpsertPlayerPOI 更新玩家个人 POI 状态，并保存整个玩家聚合文档。
func (s *MongoPlayerStore) UpsertPlayerPOI(ctx context.Context, playerID string, target *poi.Poi) error {
	document, err := s.LoadPlayer(ctx, playerID)
	if err != nil {
		return err
	}
	for index, existing := range document.POIs {
		if existing.ID == target.ID {
			document.POIs[index] = target
			return s.savePlayer(ctx, document)
		}
	}
	document.POIs = append(document.POIs, target)
	return s.savePlayer(ctx, document)
}

// LoadPlayerPosition 读取玩家最近一次持久化的世界坐标。
func (s *MongoPlayerStore) LoadPlayerPosition(ctx context.Context, playerID string) (room.Position, bool, error) {
	document, err := s.LoadPlayer(ctx, playerID)
	if err != nil {
		return room.Position{}, false, err
	}
	if document.Position == nil {
		return room.Position{}, false, nil
	}
	return *document.Position, true, nil
}

// UpsertPlayerPosition 保存玩家当前世界坐标，并与背包和 POI 共用同一个玩家文档。
func (s *MongoPlayerStore) UpsertPlayerPosition(ctx context.Context, playerID string, position room.Position) error {
	document, err := s.LoadPlayer(ctx, playerID)
	if err != nil {
		return err
	}
	position.PlayerID = document.ID
	document.Position = &position
	return s.savePlayer(ctx, document)
}

// MigrateLegacyData 将旧 poi_states 与 backpack 集合中的数据并入 players 文档，并在成功后删除旧集合。
// POI 旧结构没有玩家字段，因此归入默认玩家；旧背包按每条记录的 player_id 分组迁移，避免丢失其它玩家数据。
func (s *MongoPlayerStore) MigrateLegacyData(ctx context.Context, legacyPoiCollection, legacyInventoryCollection string) error {
	document, err := s.LoadPlayer(ctx, s.defaultPlayer)
	if err != nil {
		return err
	}
	dirty := false
	if len(document.POIs) == 0 && legacyPoiCollection != "" {
		cursor, findErr := s.db.Collection(legacyPoiCollection).Find(ctx, bson.M{})
		if findErr != nil {
			return findErr
		}
		var records []*poi.Poi
		if findErr := cursor.All(ctx, &records); findErr != nil {
			cursor.Close(ctx)
			return findErr
		}
		cursor.Close(ctx)
		for _, record := range records {
			if record != nil {
				document.POIs = append(document.POIs, record)
			}
		}
		dirty = len(records) > 0
	}
	if len(document.Inventory) == 0 && legacyInventoryCollection != "" {
		cursor, findErr := s.db.Collection(legacyInventoryCollection).Find(ctx, bson.M{"player_id": document.ID})
		if findErr != nil {
			return findErr
		}
		var records []*item.Stack
		if findErr := cursor.All(ctx, &records); findErr != nil {
			cursor.Close(ctx)
			return findErr
		}
		cursor.Close(ctx)
		for _, record := range records {
			if record != nil {
				record.PlayerID = document.ID
				document.Inventory = append(document.Inventory, record)
			}
		}
		dirty = dirty || len(records) > 0
	}
	if dirty {
		if err := s.savePlayer(ctx, document); err != nil {
			return err
		}
	}
	if legacyInventoryCollection != "" && legacyInventoryCollection != s.coll.Name() {
		cursor, findErr := s.db.Collection(legacyInventoryCollection).Find(ctx, bson.M{})
		if findErr != nil {
			return findErr
		}
		var records []*item.Stack
		if findErr := cursor.All(ctx, &records); findErr != nil {
			cursor.Close(ctx)
			return findErr
		}
		cursor.Close(ctx)
		grouped := make(map[string][]*item.Stack)
		for _, record := range records {
			if record == nil {
				continue
			}
			playerID := record.PlayerID
			if playerID == "" {
				playerID = s.defaultPlayer
			}
			grouped[playerID] = append(grouped[playerID], record)
		}
		for playerID, stacks := range grouped {
			target, loadErr := s.LoadPlayer(ctx, playerID)
			if loadErr != nil {
				return loadErr
			}
			if len(target.Inventory) != 0 {
				continue
			}
			for _, stack := range stacks {
				stack.PlayerID = target.ID
				target.Inventory = append(target.Inventory, stack)
			}
			if err := s.savePlayer(ctx, target); err != nil {
				return err
			}
		}
	}
	if legacyPoiCollection != "" && legacyPoiCollection != s.coll.Name() {
		if err := s.db.Collection(legacyPoiCollection).Drop(ctx); err != nil && !isNamespaceNotFound(err) {
			return err
		}
	}
	if legacyInventoryCollection != "" && legacyInventoryCollection != s.coll.Name() {
		if err := s.db.Collection(legacyInventoryCollection).Drop(ctx); err != nil && !isNamespaceNotFound(err) {
			return err
		}
	}
	return nil
}

// isNamespaceNotFound 判断 MongoDB 删除不存在集合时返回的命令错误，确保迁移可重复执行。
func isNamespaceNotFound(err error) bool {
	var commandErr mongo.CommandError
	if errors.As(err, &commandErr) && commandErr.Code == 26 {
		return true
	}
	return strings.Contains(strings.ToLower(err.Error()), "namespace not found")
}

// MigrateLegacyDefaultData 保留旧方法名兼容外部启动脚本，实际执行全量迁移并删除旧集合。
func (s *MongoPlayerStore) MigrateLegacyDefaultData(ctx context.Context, legacyPoiCollection, legacyInventoryCollection string) error {
	return s.MigrateLegacyData(ctx, legacyPoiCollection, legacyInventoryCollection)
}

// ItemStore 返回背包接口适配器，使业务层无需依赖 Mongo 实现细节。
func (s *MongoPlayerStore) ItemStore() *PlayerItemStore { return &PlayerItemStore{store: s} }

// PoiStore 返回 POI 接口适配器，使业务层继续使用既有 Store 抽象。
func (s *MongoPlayerStore) PoiStore() *PlayerPoiStore { return &PlayerPoiStore{store: s} }

// PlayerItemStore 将玩家聚合存储适配为 service.ItemStore 所需的方法集合。
type PlayerItemStore struct{ store *MongoPlayerStore }

// LoadAll 读取指定玩家的完整背包。
func (s *PlayerItemStore) LoadAll(ctx context.Context, playerID string) ([]*item.Stack, error) {
	return s.store.LoadInventory(ctx, playerID)
}

// Upsert 写入指定玩家的一叠物品。
func (s *PlayerItemStore) Upsert(ctx context.Context, stack *item.Stack) error {
	return s.store.UpsertInventoryStack(ctx, stack)
}

// PlayerPoiStore 将玩家聚合存储适配为 POI Store 及玩家作用域扩展接口。
type PlayerPoiStore struct{ store *MongoPlayerStore }

// LoadAll 读取默认玩家的 POI，兼容服务启动播种和调试接口。
func (s *PlayerPoiStore) LoadAll(ctx context.Context) ([]*poi.Poi, error) {
	return s.store.LoadPlayerPOIs(ctx, s.store.defaultPlayer)
}

// Upsert 写入默认玩家的 POI，兼容旧的默认玩家业务调用。
func (s *PlayerPoiStore) Upsert(ctx context.Context, target *poi.Poi) error {
	return s.store.UpsertPlayerPOI(ctx, s.store.defaultPlayer, target)
}

// LoadAllForPlayer 读取指定玩家的 POI。
func (s *PlayerPoiStore) LoadAllForPlayer(ctx context.Context, playerID string) ([]*poi.Poi, error) {
	return s.store.LoadPlayerPOIs(ctx, playerID)
}

// UpsertForPlayer 写入指定玩家的 POI。
func (s *PlayerPoiStore) UpsertForPlayer(ctx context.Context, playerID string, target *poi.Poi) error {
	return s.store.UpsertPlayerPOI(ctx, playerID, target)
}

// LoadPlayerPosition 读取默认玩家聚合中的最近坐标，供 Service 位置接口转发。
func (s *PlayerPoiStore) LoadPlayerPosition(ctx context.Context, playerID string) (room.Position, bool, error) {
	return s.store.LoadPlayerPosition(ctx, playerID)
}

// UpsertPlayerPosition 保存指定玩家坐标，供 Service 位置接口转发。
func (s *PlayerPoiStore) UpsertPlayerPosition(ctx context.Context, playerID string, position room.Position) error {
	return s.store.UpsertPlayerPosition(ctx, playerID, position)
}
