// Package store 定义 POI 完整记录的持久化抽象与 MongoDB 实现。
package store

import (
	"context"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"prometheus/internal/poi"
	"time"
)

// Store 是 POI 记录持久化抽象。
type Store interface {
	LoadAll(context.Context) ([]*poi.Poi, error)
	Upsert(context.Context, *poi.Poi) error
}

// MongoStore 使用 MongoDB 持久化 POI 记录。
type MongoStore struct{ coll *mongo.Collection }

// NewMongoStore 连接 MongoDB 并为 chunk_id 建立索引。
func NewMongoStore(ctx context.Context, uri, db, coll string) (*MongoStore, error) {
	client, err := mongo.Connect(ctx, options.Client().ApplyURI(uri).SetServerSelectionTimeout(5*time.Second))
	if err != nil {
		return nil, err
	}
	if err := client.Ping(ctx, nil); err != nil {
		return nil, err
	}
	mc := client.Database(db).Collection(coll)
	if _, err := mc.Indexes().CreateOne(ctx, mongo.IndexModel{Keys: bson.D{{Key: "chunk_id", Value: 1}}}); err != nil {
		return nil, err
	}
	return &MongoStore{coll: mc}, nil
}

// LoadAll 读取集合内全部 POI 记录。
func (m *MongoStore) LoadAll(ctx context.Context) ([]*poi.Poi, error) {
	cur, err := m.coll.Find(ctx, bson.D{})
	if err != nil {
		return nil, err
	}
	defer cur.Close(ctx)
	var pois []*poi.Poi
	if err := cur.All(ctx, &pois); err != nil {
		return nil, err
	}
	return pois, nil
}

// Upsert 按 ID 整条覆盖写入。
func (m *MongoStore) Upsert(ctx context.Context, p *poi.Poi) error {
	_, err := m.coll.ReplaceOne(ctx, bson.M{"_id": p.ID}, p, options.Replace().SetUpsert(true))
	return err
}
