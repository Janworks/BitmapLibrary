﻿using IORAMHelper;
using System;
using System.Drawing;

namespace BitmapLibrary
{
	/// <summary>
	/// Definiert einen Ladealgorithmus für unkomprimierte Bitmaps. Unterstützt werden die Bitzahlen 8 und 24, wobei immer auf 8-Bit reduziert wird. Ausgegeben werden nur Bottom-Up-Bitmaps.
	/// </summary>
	public class BitmapLoader
	{
		/// <summary>
		/// Der Bitmap-Header.
		/// </summary>
		private Header _header;

		/// <summary>
		/// Die Bitmap-Farbtabelle.
		/// </summary>
		private ColorTable _colorTable;

		/// <summary>
		/// Die Bilddaten in ihrer schlussendlich binär geschriebenen Form (d.h. mit Füllbytes).
		/// </summary>
		private byte[] _imageDataBin;

		/// <summary>
		/// Die Bilddaten in binärer Form, allerdings ohne Füllbytes (immer Top-Down).
		/// </summary>
		private byte[] _imageData;

		/// <summary>
		/// Erstellt ein neues Bitmap mit den angegebenen Abmessungen. Diese Bitmaps werden am Ende immer nach dem Bottom-Up-Verfahren geschrieben.
		/// </summary>
		/// <param name="width">Die Breite des zu erstellenden Bitmaps.</param>
		/// <param name="height">Die Höhe des zu erstellenden Bitmaps.</param>
		/// <param name="pal">Gibt die zu verwendende 256er-Farbtabelle an. Standardwert ist die 50500er-Farbtabelle.</param>
		public BitmapLoader(int width, int height, ColorTable pal)
		{
			// Header initialisieren
			_header = new Header();
			_header.height = Math.Abs(height);
			_header.width = Math.Abs(width);

			// Farbtabelle initialisieren
			_colorTable = pal;

			// Bilddaten-Array initialisieren
			_imageData = new byte[_header.width * _header.height];
		}

		/// <summary>
		/// Lädt die angegebene Bitmap-Datei.
		/// </summary>
		/// <param name="filename">Der Pfad zur zu ladenden Bitmap-Datei.</param>
		/// <param name="pal">Optional. Gibt die zu verwendende 256er-Farbtabelle an. Sonst wird die entweder die im Bitmap angegebene oder die 50500er-Farbtabelle verwendet.</param>
		/// <param name="readFileHeader">Optional. Gibt an, ob der Dateiheader gelesen werden oder direkt mit der BITMAPINFOHEADER-Struktur begonnen werden soll.</param>
		public BitmapLoader(string filename, JASCPalette pal = null, bool readFileHeader = true)
			: this(new RAMBuffer(filename), pal, readFileHeader) { }


		/// <summary>
		/// Lädt die Bitmap-Datei aus dem angegebenen Puffer.
		/// </summary>
		/// <param name="buffer">Der Puffer, aus dem die Bitmap-Datei gelesen werden soll.</param>
		/// <param name="pal">Optional. Gibt die zu verwendende 256er-Farbtabelle an. Sonst wird die entweder die im Bitmap angegebene oder die 50500er-Farbtabelle verwendet.</param>
		/// <param name="readFileHeader">Optional. Gibt an, ob der Dateiheader gelesen werden oder direkt mit der BITMAPINFOHEADER-Struktur begonnen werden soll.</param>
		public BitmapLoader(RAMBuffer buffer, JASCPalette pal = null, bool readFileHeader = true)
		{
			// Header laden
			_header = new Header();
			if(readFileHeader)
			{
				_header.type = buffer.ReadUShort();
				_header.fileSize = buffer.ReadUInteger();
				_header.reserved = buffer.ReadUInteger();
				_header.offsetData = buffer.ReadUInteger();
			}
			else
			{
				_header.type = 0x424D;
				_header.fileSize = 0;
				_header.reserved = 0;
				_header.offsetData = 54;
			}
			_header.imageHeaderSize = buffer.ReadUInteger();
			_header.width = buffer.ReadInteger();
			_header.height = buffer.ReadInteger();
			_header.layerCount = buffer.ReadUShort();
			_header.bitsPerPixel = buffer.ReadUShort();
			_header.compression = buffer.ReadUInteger();
			_header.size = buffer.ReadUInteger();
			_header.xDPI = buffer.ReadInteger();
			_header.yDPI = buffer.ReadInteger();
			_header.colorCount = buffer.ReadUInteger();
			_header.colorImportantCount = buffer.ReadUInteger();

			// Farbtabellenanzahl nachjustieren
			if(_header.colorCount == 0 && _header.bitsPerPixel == 8)
				_header.colorCount = 256;

			// Farbtabelle laden
			bool needAdjustColorTable = false;
			if(_header.colorCount > 0)
			{
				// Bildfarbtabelle laden
				_colorTable = new ColorTable(ref buffer, _header.colorCount);

				// Falls eine Palette übergeben wurde, diese mit der Bildtabelle vergleichen
				if(pal == null || pal._farben.GetLength(0) != 256)
					needAdjustColorTable = true;
				else
					for(int i = 0; i < 256; ++i)
					{
						// Farben vergleichen
						Color aktF = _colorTable[i];
						if(pal._farben[i, 0] != aktF.R || pal._farben[i, 1] != aktF.G || pal._farben[i, 2] != aktF.B)
						{
							// Farbtabellen unterscheiden sich
							needAdjustColorTable = true;
							break;
						}
					}
			}
			else
			{
				// Bei 24-Bit-Bitmaps wird die Farbtabelle später geladen
				_colorTable = null;
			}

			// Nach Bitzahl unterscheiden
			if(_header.bitsPerPixel == 8)
			{
				// Bilddatenbreite ggf. auf ein Vielfaches von 4 Bytes erhöhen
				int width = _header.width; // Hilfsvariable zur Performanceerhöhung (immer gleichwertig mit _header.width)
				int width2 = width;
				while(width2 % 4 != 0)
				{
					width2++;
				}

				// Binäre Original-Bilddaten einlesen
				_imageDataBin = buffer.ReadByteArray(width2 * Math.Abs(_header.height));

				// Neues Bilddaten-Array anlegen (ohne Füllbytes)
				_imageData = new byte[width * Math.Abs(_header.height)];

				// Richtung bestimmen
				bool dirTopDown = (_header.height < 0);

				// Der bisher nächste Farbindex
				byte nearestIndex = 0;

				// Der Abstand zum bisher nächsten Farbindex
				double nearestDistance;

				// Der aktuelle Farbabstand
				double tempDistance = 0.0;

				// Bilddaten abrufen
				int height2 = Math.Abs(_header.height);
				for(int x = 0; x < width2; x++)
				{
					for(int y = 0; y < height2; y++)
					{
						// Wenn es sich bei dem aktuellen Pixel um kein Füllbyte handelt, diesen übernehmen
						if(x < width)
						{
							// Pixel abrufen
							byte aktCol = _imageDataBin[y * width2 + x];

							// TODO: 0-Indizes in 255 umwandeln??

							// Falls nötig, Farben vergleichen
							if(needAdjustColorTable)
							{
								// Alle Farbwerte abrufen
								byte aktB = _colorTable[aktCol].B;
								byte aktG = _colorTable[aktCol].G;
								byte aktR = _colorTable[aktCol].R;

								// Die zur Pixelfarbe nächste Palettenfarbe suchen
								{
									// Werte zurücksetzen
									nearestIndex = 0;
									nearestDistance = 441.673; // Anfangswert: maximaler möglicher Abstand

									// Alle Einträge durchgehen
									for(int i = 0; i < 256; i++)
									{
										// Aktuelle Paletten-RGB-Werte abrufen
										byte pR = pal._farben[i, 0];
										byte pG = pal._farben[i, 1];
										byte pB = pal._farben[i, 2];

										// Gleiche Einträge sofort filtern
										if(aktR == pR && aktB == pB && aktG == pG)
										{
											// Paletten-Index überschreiben
											nearestIndex = (byte)i;

											// Fertig
											break;
										}

										// Abstand berechnen (Vektorlänge im dreidimensionalen RGB-Farbraum)
										tempDistance = Math.Sqrt(Math.Pow(aktR - pR, 2) + Math.Pow(aktG - pG, 2) + Math.Pow(aktB - pB, 2));

										// Vergleichen
										if(tempDistance < nearestDistance)
										{
											// Index merken
											nearestDistance = tempDistance;
											nearestIndex = (byte)i;
										}
									}

									// Paletten-Index überschreiben
									aktCol = nearestIndex;
								}
							} // Ende Adjust-ColorTable

							// Pixel zum Hauptbildarray hinzufügen und dabei nach Top-Down / Bottom-Up unterscheiden
							_imageData[(dirTopDown ? y : height2 - y - 1) * width + x] = aktCol;
						}
					}
				}
			}
			else if(_header.bitsPerPixel == 24)
			{
				// Es handelt sich um ein 24-Bit-Bitmap, somit muss eine Farbtabelle eingeführt werden
				{
					// Farbpalettenreader abrufen
					JASCPalette tempPal;
					if(pal == null)
						tempPal = new JASCPalette(new RAMBuffer(BitmapLibrary.Properties.Resources.pal50500));
					else
						tempPal = pal;

					// Farbpaletteninhalt in eigene Farbtabelle schreiben
					_colorTable = new ColorTable();
					for(int i = 0; i < tempPal._farben.GetLength(0); i++)
					{
						// Eintrag in Tabelle einfügen
						_colorTable[i] = Color.FromArgb(tempPal._farben[i, 0], tempPal._farben[i, 1], tempPal._farben[i, 2]);

						// Sicherheitshalber bei i = 255 abbrechen (falls Palette zu groß sein sollte)
						if(i == 255)
							break;
					}
				}

				// Bilddatenbreite ggf. auf ein Vielfaches von 4 Bytes erhöhen
				int width = _header.width; // Hilfsvariable zur Performanceerhöhung (immer gleichwertig mit _header.width)
				int fillBytes = 0;
				while(((width * 3) + fillBytes) % 4 != 0)
				{
					fillBytes++;
				}

				// Binäre Original-Bilddaten einlesen
				_imageDataBin = buffer.ReadByteArray((3 * width + fillBytes) * Math.Abs(_header.height));

				// Neues Bilddaten-Array anlegen (ohne Füllbytes)
				_imageData = new byte[width * Math.Abs(_header.height)];

				// Richtung bestimmen
				bool dirTopDown = (_header.height < 0);

				// Der bisher nächste Farbindex
				byte nearestIndex = 0;

				// Der Abstand zum bisher nächsten Farbindex
				double nearestDistance;

				// Der aktuelle Farbabstand
				double tempDistance = 0.0;

				// Bilddaten abrufen
				int height2 = Math.Abs(_header.height);
				for(int x = 0; x < width; x++)
				{
					for(int y = 0; y < height2; y++)
					{
						// Pixel abrufen
						byte aktB = _imageDataBin[y * (3 * width + fillBytes) + 3 * x];
						byte aktG = _imageDataBin[y * (3 * width + fillBytes) + 3 * x + 1];
						byte aktR = _imageDataBin[y * (3 * width + fillBytes) + 3 * x + 2];

						// Die zur Pixelfarbe nächste Palettenfarbe suchen
						{
							// Werte zurücksetzen
							nearestIndex = 0;
							nearestDistance = 441.673; // Anfangswert: maximaler möglicher Abstand

							// Alle Einträge durchgehen
							for(int i = 0; i < 256; i++)
							{
								// Aktuelle Paletten-RGB-Werte abrufen
								byte pR = _colorTable[i].R;
								byte pG = _colorTable[i].G;
								byte pB = _colorTable[i].B;

								// Gleiche Einträge sofort filtern
								if(aktR == pR && aktB == pB && aktG == pG)
								{
									// Pixel zum Hauptbildarray hinzufügen und dabei nach Top-Down / Bottom-Up unterscheiden
									_imageData[(dirTopDown ? y : height2 - y - 1) * width + x] = (byte)i;

									// Fertig
									break;
								}

								// Abstand berechnen (Vektorlänge im dreidimensionalen RGB-Farbraum)
								tempDistance = Math.Sqrt(Math.Pow(aktR - pR, 2) + Math.Pow(aktG - pG, 2) + Math.Pow(aktB - pB, 2));

								// Vergleichen
								if(tempDistance < nearestDistance)
								{
									// Index merken
									nearestDistance = tempDistance;
									nearestIndex = (byte)i;
								}
							}

							// Pixel zum Hauptbildarray hinzufügen und dabei nach Top-Down / Bottom-Up unterscheiden
							_imageData[(dirTopDown ? y : height2 - y - 1) * width + x] = nearestIndex;
						}
					}

					// Ggf. Füllbytes überspringen (bei Dateiende nicht)
					if(buffer.Position < buffer.Length - fillBytes)
						buffer.Position = (buffer.Position + fillBytes);
				}
			}
		}
		
		/// <summary>
		 /// Speichert die enthaltene Bitmap in den angegebenen Puffer.
		 /// </summary>
		 /// <param name="buffer">Der Puffer, in den das Bild gespeichert werden soll.</param>
		 /// <param name="writeFileHeader">Optional. Gibt an, ob der Dateiheader geschrieben werden oder direkt mit der BITMAPINFOHEADER-Struktur begonnen werden soll.</param>
		public void SaveToBuffer(RAMBuffer buffer, bool writeFileHeader = true)
		{
			// Bilddatenbreite ggf. auf ein Vielfaches von 4 Bytes erhöhen
			int width = _header.width; // Hilfsvariable zur Performanceerhöhung (immer gleichwertig mit _header.width)
			int width2 = width;
			while(width2 % 4 != 0)
			{
				width2++;
			}

			// Bilddaten-Binär-Zielarray erstellen
			_imageDataBin = new byte[width2 * Math.Abs(_header.height)];

			// Bilddaten in Zielarray schreiben
			int height2 = Math.Abs(_header.height);
			for(int x = 0; x < width2; x++) // Start: Links
			{
				for(int y = 0; y < height2; y++) // Start: Oben
				{
					if(x >= width)
					{
						// Falls x außerhalb der Bildbreite liegt, Füllbyte einsetzen
						_imageDataBin[y * width2 + x] = 0;
					}
					else
					{
						// Normaler Pixel: Farbtabellenindex schreiben, dabei Bottom-Up-Richtung beachten
						_imageDataBin[y * width2 + x] = _imageData[(height2 - y - 1) * width + x];
					}
				}
			}

			// Header vorbereiten (einige wurden zwar schon definiert, aber lieber alle beisammen)
			_header.type = 19778;
			_header.fileSize = (uint)(44 + 256 * 4 + _imageDataBin.Length);
			_header.reserved = 0;
			_header.offsetData = (uint)(44 + 256 * 4);
			_header.imageHeaderSize = 40;
			_header.width = width;
			_header.height = height2;
			_header.layerCount = 1;
			_header.bitsPerPixel = 8;
			_header.compression = Header.COMPR_RGB;
			_header.size = (uint)(height2 * width);
			_header.xDPI = 0;
			_header.yDPI = 0;
			_header.colorCount = 0;
			_header.colorImportantCount = 0;

			// Header schreiben
			if(writeFileHeader)
			{
				buffer.WriteUShort(_header.type);
				buffer.WriteUInteger(_header.fileSize);
				buffer.WriteUInteger(_header.reserved);
				buffer.WriteUInteger(_header.offsetData);
			}
			buffer.WriteUInteger(_header.imageHeaderSize);
			buffer.WriteInteger(_header.width);
			buffer.WriteInteger(_header.height);
			buffer.WriteUShort(_header.layerCount);
			buffer.WriteUShort(_header.bitsPerPixel);
			buffer.WriteUInteger(_header.compression);
			buffer.WriteUInteger(_header.size);
			buffer.WriteInteger(_header.xDPI);
			buffer.WriteInteger(_header.yDPI);
			buffer.WriteUInteger(_header.colorCount);
			buffer.WriteUInteger(_header.colorImportantCount);

			// Farbtabelle schreiben
			_colorTable.ToBinary(ref buffer);

			// Bilddaten schreiben
			buffer.Write(_imageDataBin);
		}

		/// <summary>
		/// Speichert die enthaltene Bitmap in die angegebene Datei.
		/// </summary>
		/// <param name="filename">Die Datei, in die das Bild gespeichert werden soll.</param>
		/// <param name="writeFileHeader">Optional. Gibt an, ob der Dateiheader geschrieben werden oder direkt mit der BITMAPINFOHEADER-Struktur begonnen werden soll.</param>
		public void SaveToFile(string filename, bool writeFileHeader = true)
		{
			// Puffer-Objekt erstellen und Daten hineinschreiben
			RAMBuffer buffer = new RAMBuffer();
			SaveToBuffer(buffer, writeFileHeader);

			// Als Datei speichern
			buffer.Save(filename);
		}

		/// <summary>
		/// Gibt den Farbpalettenindex an der Position (x, y) zurück oder legt diesen fest.
		/// </summary>
		/// <param name="x">Die X-Koordinate des betreffenden Pixels.</param>
		/// <param name="y">Die Y-Koordinate des betreffenden Pixels.</param>
		/// <returns></returns>
		public byte this[int x, int y]
		{
			get
			{
				// Sicherheitsüberprüfung
				if(x >= _header.width || y >= Math.Abs(_header.height))
				{
					// Fehler
					throw new ArgumentOutOfRangeException("Die angegebene Position liegt nicht innerhalb des Bildes!");
				}

				// Wert zurückgeben
				return _imageData[y * _header.width + x];
			}
			set
			{
				// Sicherheitsüberprüfung
				if(x >= _header.width || y >= Math.Abs(_header.height))
				{
					// Fehler
					throw new ArgumentOutOfRangeException("Die angegebene Position liegt nicht innerhalb des Bildes!");
				}

				// Farbwert zuweisen
				_imageData[y * _header.width + x] = value;
			}
		}

		/// <summary>
		/// Gibt den Farbpalettenindex an der angegebenen Position zurück oder legt diesen fest.
		/// </summary>
		/// <param name="pos">Die Position des betreffenden Pixels.</param>
		/// <returns></returns>
		public byte this[Point pos]
		{
			get
			{
				// Wert zurückgeben
				return this[pos.X, pos.Y];
			}
			set
			{
				// Wert zuweisen
				this[pos.X, pos.Y] = value;
			}
		}

		/// <summary>
		/// Ruft die Bildbreite ab.
		/// </summary>
		public int Width
		{
			get
			{
				// Breite zurückgeben
				return _header.width;
			}
		}

		/// <summary>
		/// Ruft die Bildhöhe ab.
		/// </summary>
		public int Height
		{
			get
			{
				// Breite zurückgeben
				return Math.Abs(_header.height);
			}
		}

		#region Strukturen

		/// <summary>
		/// Definiert den Bitmap-Header.
		/// </summary>
		private struct Header
		{
			/// <summary>
			/// Definiert eine unkomprimierte Bitmap-Datei.
			/// </summary>
			internal const uint COMPR_RGB = 0;

			/// <summary>
			/// Definiert den Dateityp. Immer 19778 ("BM").
			/// </summary>
			internal ushort type;

			/// <summary>
			/// Die Größe der Bitmap-Datei.
			/// </summary>
			internal uint fileSize;

			/// <summary>
			/// 4 reservierte Bytes.
			/// </summary>
			internal uint reserved;

			/// <summary>
			/// Das Offset der Pixeldaten.
			/// </summary>
			internal uint offsetData;

			/// <summary>
			/// Die Länge des Bildheaders. Immer 40.
			/// </summary>
			internal uint imageHeaderSize;

			/// <summary>
			/// Die Breite des Bilds.
			/// </summary>
			internal int width;

			/// <summary>
			/// Die Höhe des Bilds.
			/// Vorsicht: Wenn die Höhe positiv ist, wurde das Bild von unten nach oben geschrieben, bei negativer Höhe von oben nach unten.
			/// </summary>
			internal int height;

			/// <summary>
			/// Die Anzahl der Farbebenen. Immer 1.
			/// </summary>
			internal ushort layerCount;

			/// <summary>
			/// Die Anzahl der Bits pro Pixel.
			/// </summary>
			internal ushort bitsPerPixel;

			/// <summary>
			/// Die verwendete Bildkompression.
			/// </summary>
			internal uint compression;

			/// <summary>
			/// Die Größe der Bilddaten.
			/// </summary>
			internal uint size;

			/// <summary>
			/// Die horizontale Auflösung des Zielausgabegeräts in Pixeln pro Meter. Meist 0.
			/// </summary>
			internal int xDPI;

			/// <summary>
			/// Die vertikale Auflösung des Zielausgabegeräts in Pixeln pro Meter. Meist 0.
			/// </summary>
			internal int yDPI;

			/// <summary>
			/// Die Anzahl der Farben in der Farbtabelle. Meist 0 (bedeutet Maximalzahl, d.h. 2 ^ bitsPerPixel).
			/// </summary>
			internal uint colorCount;

			/// <summary>
			/// Die Anzahl der tatsächlich im Bild verwendeten Farben. Meist 0.
			/// </summary>
			internal uint colorImportantCount;
		}

		#endregion Strukturen
	}
}