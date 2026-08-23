package poi

import (
	"encoding/json"
	"os"
)

// ExportItem 是策划导出 JSON（PoiExport.json）中一条 POI 的完整定义。
// 服务器据此入库：ID（同步键/主键）、Region、类型、位置、旋转与空间分区 ChunkId。
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
	return root.Pois, nil
}
