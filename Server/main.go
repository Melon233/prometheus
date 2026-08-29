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

func main() {
	var (
		listenAddr   = flag.String("addr", "127.0.0.1:9000", "TCP 监听地址")
		mongoURI     = flag.String("mongo", "mongodb://admin:admin123@localhost:27017/?authSource=admin", "MongoDB 连接串")
		mongoDB      = flag.String("db", "prometheus", "数据库名")
		mongoColl    = flag.String("coll", "poi_states", "POI 集合名")
		backpackColl = flag.String("backpack", "backpack", "背包集合名")
		exportPath   = flag.String("export", "../Assets/Resources/Config/PoiExport.json", "策划导出 POI JSON 路径")
		itemsPath    = flag.String("items", "config/items.json", "物品配置 JSON 路径")
	)
	flag.Parse()

	ctx := context.Background()

	// 1. 连接 MongoDB。
	st, err := store.NewMongoStore(ctx, *mongoURI, *mongoDB, *mongoColl)
	if err != nil {
		log.Fatalf("connect mongo: %v", err)
	}
	log.Printf("mongo connected: db=%s", *mongoDB)

	// 2. 加载物品配置并初始化背包
	itemConfig, err := item.LoadConfig(*itemsPath)
	if err != nil {
		log.Fatalf("load items %s: %v", *itemsPath, err)
	}
	itemStore, err := store.NewItemStore(ctx, *mongoURI, *mongoDB, *backpackColl)
	if err != nil {
		log.Fatalf("connect backpack store: %v", err)
	}
	inventory := service.NewInventory(itemStore, itemConfig, defaultPlayerID)
	if err := inventory.Load(ctx); err != nil {
		log.Fatalf("load backpack: %v", err)
	}
	log.Printf("items loaded: %d defs, backpack ready", len(itemConfig.Items))

	// 3. 读取策划导出并播种
	exported, err := poi.LoadExport(*exportPath)
	if err != nil {
		log.Fatalf("load export %s: %v", *exportPath, err)
	}
	svc := service.New(st, inventory, itemConfig)
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
