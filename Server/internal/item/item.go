// Package item 定义背包物品的领域模型、配置与加载。
package item

import (
	"encoding/json"
	"os"
)

// 物品 ID 常量（与 config/items.json 一致）。
const (
	IDSword      = "Sword"      // 长剑
	IDArmor      = "Armor"      // 护甲
	IDApple      = "Apple"      // 苹果
	IDAnemoculus = "Anemoculus" // 风神瞳
	IDExpBook    = "ExpBook"    // 经验书
)

// Def 是物品定义（配置表）。
type Def struct {
	ID       string `json:"id"`
	Name     string `json:"name"`
	Category string `json:"category"` // weapon/equipment/food/special/consumable
	Quality  int32  `json:"quality"`  // 1-5
}

// StatueConfig 神像供奉数值。
type StatueConfig struct {
	ProgressPerOculus int32 `json:"progress_per_oculus"` // 每个风神瞳提供的进度
	LevelThreshold    int32 `json:"level_threshold"`     // 每级所需进度
}

// Config 是物品与神像供奉配置。
type Config struct {
	Items  []Def        `json:"items"`
	Statue StatueConfig `json:"statue"`
}

// Stack 是背包中的一叠物品（player_id + item_id + quality 唯一）。
type Stack struct {
	PlayerID string `bson:"player_id"`
	ItemID   string `bson:"item_id"`
	Quality  int32  `bson:"quality"`
	Quantity int32  `bson:"quantity"`
}

// LoadConfig 读取物品配置 JSON。
func LoadConfig(path string) (*Config, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	var cfg Config
	if err := json.Unmarshal(data, &cfg); err != nil {
		return nil, err
	}
	return &cfg, nil
}

// FindDef 按 ID 查找物品定义。
func (c *Config) FindDef(id string) (Def, bool) {
	for _, d := range c.Items {
		if d.ID == id {
			return d, true
		}
	}
	return Def{}, false
}
