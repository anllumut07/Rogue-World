using Godot;
using System.Collections.Generic;

// M18: İl (province) veri modeli. Ülke poligonlarının alt Bölgesi;
// komşuluk grafiği ve sahiplik bilgisiyle birlikte saklanır.
// Save/Load'da CountryId (sahip ülke) değişebilir → ilhak sonrası el değiştirir.
public class ProvinceData
{
    public string Id = "?";           // benzersiz il kimliği (örn. "TR-34")
    public string Name = "?";         // il adı
    public string CountryId = "?";    // sahip ülke ISO A3 (GeoJSON zinciriyle eşleşir)
    public string Continent = "";     // kıtda (isteğe bağlı,UI filtresi için)
    public Vector2 Centroid = Vector2.Zero; // poligon merkezi (etiket ve komşuluk testi için)
    public float Area = 0f;           // kare birim (saniyal/örnekleme tabanlı)
    public List<Vector2[]> Rings = new(); // dış halka + delikler (GeoJSON uyumlu)
    public List<string> NeighborIds = new(); // komşu il id'leri (18.2'de doldurulur)
    public bool IsCore = true;        // ülkenin asal mı değil mi (yeni ilhaklarda false)
}
