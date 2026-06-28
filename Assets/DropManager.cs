using System.Collections.Generic;
using UnityEngine;

public class DropManager : MonoBehaviour
{
    private List<PlacedTile> _dropLocations = new List<PlacedTile>();
    private PlacedTile _tempPlacedTile;
    private GhostTile _currentLocation;

    public bool isCurrentlyDragging = false;

    public List<PlacedTile> GetTilesDroppedThisTurn()
    {
        return _dropLocations;
    }

    public void SetTempGrabbedTile(PlacedTile tile)
    {
        _tempPlacedTile = tile;
    }

    public bool RemovedPlacedTile(PlacedTile placedTile)
    {
        return _dropLocations.Remove(placedTile);
    }

    public void ResetLocations()
    {
        _dropLocations.Clear();
        _tempPlacedTile = null;
        _currentLocation = null;
    }

    public void SetCurrentLocation(GhostTile location)
    {
        _currentLocation = location;
    }

    public GhostTile GetCurrentLocation()
    {
        return _currentLocation;
    }

    public void ClearCurrentLocation(GhostTile location)
    {
        if (_currentLocation == location)
        {
            _currentLocation = null;
        }
    }

    public void AddLocation()
    {
        if (_tempPlacedTile != null)
        {
            if (_currentLocation != null)
            {
                _tempPlacedTile.letterPosition = _currentLocation.letterPosition;
            }

            if (!_dropLocations.Contains(_tempPlacedTile))
            {
                _dropLocations.Add(_tempPlacedTile);
            }
        }

        _tempPlacedTile = null;
    }
}