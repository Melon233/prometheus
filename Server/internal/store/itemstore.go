package store

import (
	"context"
	"time"

	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"

	"prometheus/internal/item"
)

// ItemStore 使用 MongoDB 持久化背包物品（每叠一条文档）。
type ItemStore struct {
	coll *mongo.Collection
}

// NewItemStore 连接 MongoDB 并返回背包集合。
func NewItemStore(ctx context.Context, uri, db, coll string) (*ItemStore, error) {
	client, err := mongo.Connect(ctx, options.Client().ApplyURI(uri).SetServerSelectionTimeout(5*time.Second))
	if err != nil {
		return nil, err
	}
	if err := client.Ping(ctx, nil); err != nil {
		return nil, err
	}
	return &ItemStore{coll: client.Database(db).Collection(coll)}, nil
}

// LoadAll 读取指定玩家的全部物品。
func (s *ItemStore) LoadAll(ctx context.Context, playerID string) ([]*item.Stack, error) {
	cur, err := s.coll.Find(ctx, bson.M{"player_id": playerID})
	if err != nil {
		return nil, err
	}
	defer cur.Close(ctx)
	var stacks []*item.Stack
	if err := cur.All(ctx, &stacks); err != nil {
		return nil, err
	}
	return stacks, nil
}

// Upsert 按 (player_id, item_id, quality) 写入或更新一叠物品。
func (s *ItemStore) Upsert(ctx context.Context, st *item.Stack) error {
	filter := bson.M{"player_id": st.PlayerID, "item_id": st.ItemID, "quality": st.Quality}
	_, err := s.coll.ReplaceOne(ctx, filter, st, options.Replace().SetUpsert(true))
	return err
}
