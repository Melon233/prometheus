package poi

import (
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
)

// ExportItem 是策划导出 JSON（PoiExport.json）中一条 POI 的完整定义。
// 服务器据此入库：ID（不可变 UUID 同步键/主键）、Region、类型、位置、旋转与空间分区 ChunkId。
type ExportItem struct {
	ID       string `json:"Id"`
	Region   string `json:"Region"`
	PoiType  int32  `json:"PoiType"`
	Position Vec3   `json:"Position"`
	Rotation Quat   `json:"Rotation"`
	ChunkID  int32  `json:"ChunkId"`
}

// exportRoot 是 PoiExport.json 的根结构（{"pois": [...]}）。
type exportRoot struct {
	Pois []ExportItem `json:"pois"`
}

// LoadExport 从磁盘读取策划导出的 POI 定义 JSON，解析出服务器入库所需的 POI 列表。
func LoadExport(path string) ([]ExportItem, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	var root exportRoot
	if err := json.Unmarshal(data, &root); err != nil {
		return nil, err
	}
	seen := make(map[string]struct{}, len(root.Pois))
	for index := range root.Pois {
		id := root.Pois[index].ID
		if !IsUUID(id) {
			return nil, fmt.Errorf("poi export item %d has invalid UUID %q", index, id)
		}
		if _, exists := seen[id]; exists {
			return nil, fmt.Errorf("poi export contains duplicate UUID %q", id)
		}
		seen[id] = struct{}{}
	}
	return root.Pois, nil
}

// IsUUID 判断 POI 是否为 Unity 导出的 32 位无分隔小写 UUID。
func IsUUID(value string) bool {
	if len(value) != 32 {
		return false
	}
	if value != toLowerASCII(value) {
		return false
	}
	_, err := hex.DecodeString(value)
	return err == nil
}

// toLowerASCII 仅处理 UUID 所需的 ASCII 大写字符，避免引入额外字符串规范化语义。
func toLowerASCII(value string) string {
	bytes := []byte(value)
	for index, char := range bytes {
		if char >= 'A' && char <= 'F' {
			bytes[index] = char + ('a' - 'A')
		}
	}
	return string(bytes)
}
