namespace MachineRepair {
    public enum CellPlaceability {
        Blocked,            // blocks placement
        Placeable,        // can place and path freely
        ConnectorsOnly,  // wires and pipes but no components
        Display         // Display Row of espresso machine
    }

    public enum CellFillState {
        None,     // empty or wires; does not fill the cell
        Connection,  // a connection point cell
        Component,      // components or pipes that "occupy" the cell

    }

    public enum WireType{
        None,
        AC,
        DC,
        Signal
    }

    public enum PortType{
        Power,
        Water,
        Signal
    }


}
