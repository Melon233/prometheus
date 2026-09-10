// prometheus POI 服务器：连接 MongoDB、读取策划导出并播种、加载物品配置与背包、启动 TCP 服务。
// 启动（可被 Unity Editor 拉起）：go build -o bin/server.exe . && bin/server.exe [flags]
package main

import (
	"context"
	"flag"
	"log"

	"prometheus/internal/item"
	"prometheus/internal/netx"
	"prometheus/internal/poi"
	"prometheus/internal/service"
	"prometheus/internal/store"
)

// defaultPlayerID 单隐式玩家标识（当前无登录系统，所有背包操作针对该玩家）。
const defaultPlayerID = "default"

// localMongoOverride 临时硬编码开关：非空时覆盖 -mongo 默认值，用于把服务端接到本地部署（通常无账号）的 MongoDB。
// 留空则走 docker/mongo 带 root 账号的默认实例。仅为临时联调需要，长期方案应改用配置文件或环境变量。
const localMongoOverride = "mongodb://localhost:27017"

// dockerMongoURI 默认连接串，对应 docker/mongo/docker-compose.yml 中的带账号实例。
const dockerMongoURI = "mongodb://admin:admin123@localhost:27017/?authSource=admin"

// defaultMongoURI 解析 -mongo 的默认值：localMongoOverride 非空时优先用它，否则用 dockerMongoURI。
func defaultMongoURI() string {
	if localMongoOverride != "" {
		return localMongoOverride
	}
	return dockerMongoURI
}

func main() {
	var (
		listenAddr  = flag.String("addr", "127.0.0.1:9000", "TCP 监听地址")
		mongoURI    = flag.String("mongo", defaultMongoURI(), "MongoDB 连接串（默认见 localMongoOverride / dockerMongoURI）")
		mongoDB     = flag.String("db", "prometheus", "数据库名")
		playersColl = flag.String("players", "players", "玩家聚合集合名")
		exportPath  = flag.String("export", "../Assets/Resources/Config/PoiExport.json", "策划导出 POI JSON 路径")
		itemsPath   = flag.String("items", "config/items.json", "物品配置 JSON 路径")
	)
	flag.Parse()

	ctx := context.Background()

	// 1. 连接 MongoDB；账号、背包和个人 POI 状态统一存入 players 集合。
	playerStore, err := store.NewMongoPlayerStore(ctx, *mongoURI, *mongoDB, *playersColl, defaultPlayerID)
	if err != nil {
		log.Fatalf("connect player store: %v", err)
	}
	log.Printf("mongo connected: db=%s", *mongoDB)
	if err := playerStore.MigrateLegacyData(ctx, "poi_states", "backpack"); err != nil {
		log.Fatalf("migrate legacy player data: %v", err)
	}

	// 2. 加载物品配置并初始化背包
	itemConfig, err := item.LoadConfig(*itemsPath)
	if err != nil {
		log.Fatalf("load items %s: %v", *itemsPath, err)
	}
	inventory := service.NewInventory(playerStore.ItemStore(), itemConfig, defaultPlayerID)
	if err := inventory.Load(ctx); err != nil {
		log.Fatalf("load backpack: %v", err)
	}
	log.Printf("items loaded: %d defs, backpack ready", len(itemConfig.Items))

	// 3. 读取策划导出并播种
	exported, err := poi.LoadExport(*exportPath)
	if err != nil {
		log.Fatalf("load export %s: %v", *exportPath, err)
	}
	svc := service.New(playerStore.PoiStore(), inventory, itemConfig)
	if err := svc.Seed(ctx, exported); err != nil {
		log.Fatalf("seed: %v", err)
	}
	log.Printf("seeded: %d exported, %d total states", len(exported), len(svc.PullAll()))

	// 4. 启动 TCP 服务
	log.Printf("listening on %s", *listenAddr)
	if err := netx.New(ctx, svc).ListenAndServe(*listenAddr); err != nil {
		log.Fatalf("serve: %v", err)
	}
}
